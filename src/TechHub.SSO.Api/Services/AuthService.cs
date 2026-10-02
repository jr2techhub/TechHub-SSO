using Microsoft.EntityFrameworkCore;
using TechHub.SSO.Api.Data;
using TechHub.SSO.Core.Entities;

namespace TechHub.SSO.Api.Services;

public enum AuthOutcome
{
    Success,
    InvalidCredentials,
    LockedOut,
    TrialExpired,
    TenantDisabled,
    DuplicateEmail,
    DuplicateDomain
}

public record AuthResult(AuthOutcome Outcome, ApplicationUser? User = null, Tenant? Tenant = null)
{
    public bool Succeeded => Outcome == AuthOutcome.Success;
}

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(string email, string password, string firstName, string lastName, string tenantName, string domain);
    Task<AuthResult> ValidateCredentialsAsync(string email, Guid tenantId, string password);
    Task<ApplicationUser?> GetUserByEmailAsync(string email, Guid tenantId);
    Task<bool> IsTrialExpiredAsync(Guid tenantId);
    Task<bool> UpgradeTenantAsync(Guid tenantId, PlanType newPlan);
}

public class AuthService : IAuthService
{
    private const int TrialDays = 7;
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly SsoDbContext _context;

    public AuthService(SsoDbContext context)
    {
        _context = context;
    }

    public async Task<AuthResult> RegisterAsync(string email, string password, string firstName, string lastName, string tenantName, string domain)
    {
        email = NormalizeEmail(email);

        // El dominio del tenant debe ser único en todo el sistema.
        if (await _context.Tenants.AnyAsync(t => t.Domain == domain))
            return new AuthResult(AuthOutcome.DuplicateDomain);

        // Crear Tenant con trial de 7 días y su usuario admin en la misma transacción.
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = tenantName,
            Domain = domain,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            TrialEndDate = DateTime.UtcNow.AddDays(TrialDays),
            PlanType = PlanType.Trial
        };

        _context.Tenants.Add(tenant);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = PasswordHasher.Hash(password),
            FirstName = firstName,
            LastName = lastName,
            TenantId = tenant.Id,
            Roles = "admin",
            IsEmailConfirmed = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.ApplicationUsers.Add(user);

        try
        {
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            // Viola el índice único (TenantId, Email): email duplicado en ese tenant.
            return new AuthResult(AuthOutcome.DuplicateEmail);
        }

        return new AuthResult(AuthOutcome.Success, user, tenant);
    }

    public async Task<AuthResult> ValidateCredentialsAsync(string email, Guid tenantId, string password)
    {
        email = NormalizeEmail(email);

        // Búsqueda SIEMPRE scoped por tenant: no se asume que el email sea global.
        var user = await _context.ApplicationUsers
            .FirstOrDefaultAsync(u => u.Email == email && u.TenantId == tenantId);

        // Anti-enumeración: si el usuario no existe se ejecuta igualmente un hash
        // para igualar el coste temporal y se devuelve el mismo resultado genérico.
        if (user is null)
        {
            PasswordHasher.Verify(password, PasswordHasher.DummyHash);
            return new AuthResult(AuthOutcome.InvalidCredentials);
        }

        var now = DateTime.UtcNow;

        if (user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > now)
            return new AuthResult(AuthOutcome.LockedOut, user);

        var tenant = await _context.Tenants.FindAsync(tenantId);
        if (tenant is null || !tenant.IsActive)
            return new AuthResult(AuthOutcome.TenantDisabled, user, tenant);

        if (!PasswordHasher.Verify(password, user.PasswordHash))
        {
            user.AccessFailedCount++;
            if (user.AccessFailedCount >= MaxFailedAttempts)
            {
                user.LockoutEndUtc = now.Add(LockoutDuration);
                user.AccessFailedCount = 0;
            }
            await _context.SaveChangesAsync();
            return new AuthResult(AuthOutcome.InvalidCredentials, user, tenant);
        }

        // Éxito: resetear contador y registrar último login.
        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAt = now;

        // Expiración de trial: solo se DESACTIVA el flag de expiración devolviendo
        // el resultado correspondiente; NO se borran datos como efecto colateral del login.
        if (tenant.IsTrialExpired(now))
        {
            await _context.SaveChangesAsync();
            return new AuthResult(AuthOutcome.TrialExpired, user, tenant);
        }

        await _context.SaveChangesAsync();
        return new AuthResult(AuthOutcome.Success, user, tenant);
    }

    public async Task<ApplicationUser?> GetUserByEmailAsync(string email, Guid tenantId)
    {
        email = NormalizeEmail(email);
        return await _context.ApplicationUsers
            .FirstOrDefaultAsync(u => u.Email == email && u.TenantId == tenantId);
    }

    public Task<bool> IsTrialExpiredAsync(Guid tenantId) =>
        _context.Tenants.AnyAsync(t =>
            t.Id == tenantId &&
            t.PlanType == PlanType.Trial &&
            t.TrialEndDate != null &&
            t.TrialEndDate < DateTime.UtcNow);

    public async Task<bool> UpgradeTenantAsync(Guid tenantId, PlanType newPlan)
    {
        var tenant = await _context.Tenants.FindAsync(tenantId);
        if (tenant is null)
            return false;

        tenant.PlanType = newPlan;
        tenant.TrialEndDate = null;
        tenant.IsActive = true; // Un upgrade reactiva un tenant cuyo trial expiró.
        await _context.SaveChangesAsync();
        return true;
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();
}
