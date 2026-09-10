using System.Security.Cryptography;
using System.Text;
using TechHub.SSO.Core.Entities;

namespace TechHub.SSO.Api.Services;

public interface IAuthService
{
    Task<ApplicationUser?> RegisterAsync(string email, string password, string firstName, string lastName, string tenantName, string domain);
    Task<bool> ValidateCredentialsAsync(string email, string password);
    Task<ApplicationUser?> GetUserByEmailAsync(string email);
    Task<bool> IsTrialExpiredAsync(Guid tenantId);
    Task<bool> UpgradeTenantAsync(Guid tenantId, string newPlan);
}

public class AuthService : IAuthService
{
    private readonly SsoDbContext _context;

    public AuthService(SsoDbContext context)
    {
        _context = context;
    }

    public async Task<ApplicationUser?> RegisterAsync(string email, string password, string firstName, string lastName, string tenantName, string domain)
    {
        // Verificar si el email ya existe
        if (_context.ApplicationUsers.Any(u => u.Email == email))
            return null;

        // Crear Tenant con trial de 7 días
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = tenantName,
            Domain = domain,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            TrialEndDate = DateTime.UtcNow.AddDays(7),
            PlanType = "Trial"
        };

        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();

        // Crear usuario
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = HashPassword(password),
            FirstName = firstName,
            LastName = lastName,
            TenantId = tenant.Id,
            IsEmailConfirmed = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.ApplicationUsers.Add(user);
        await _context.SaveChangesAsync();

        return user;
    }

    public async Task<bool> ValidateCredentialsAsync(string email, string password)
    {
        var user = await _context.ApplicationUsers.FindAsync(GetUserKey(email));
        if (user == null)
            return false;

        // Verificar si el tenant está activo y no ha expirado el trial
        var tenant = _context.Tenants.Find(user.TenantId);
        if (tenant == null || !tenant.IsActive)
            return false;

        if (tenant.PlanType == "Trial" && tenant.TrialEndDate.HasValue && tenant.TrialEndDate.Value < DateTime.UtcNow)
        {
            // Desactivar tenant y limpiar datos si el trial expiró
            tenant.IsActive = false;
            await _context.SaveChangesAsync();
            return false;
        }

        return VerifyPassword(password, user.PasswordHash);
    }

    public async Task<ApplicationUser?> GetUserByEmailAsync(string email)
    {
        return await _context.ApplicationUsers.FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<bool> IsTrialExpiredAsync(Guid tenantId)
    {
        var tenant = await _context.Tenants.FindAsync(tenantId);
        return tenant != null && 
               tenant.PlanType == "Trial" && 
               tenant.TrialEndDate.HasValue && 
               tenant.TrialEndDate.Value < DateTime.UtcNow;
    }

    public async Task<bool> UpgradeTenantAsync(Guid tenantId, string newPlan)
    {
        var tenant = await _context.Tenants.FindAsync(tenantId);
        if (tenant == null)
            return false;

        tenant.PlanType = newPlan;
        tenant.TrialEndDate = null; // Quitar fecha de expiración
        await _context.SaveChangesAsync();
        return true;
    }

    private string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hashedBytes);
    }

    private bool VerifyPassword(string password, string storedHash)
    {
        var hashOfInput = HashPassword(password);
        return hashOfInput == storedHash;
    }

    private string GetUserKey(string email) => email; // Simplificado para este ejemplo
}
