using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TechHub.SSO.Api.Data;
using TechHub.SSO.Api.Services;
using TechHub.SSO.Core.Entities;
using Xunit;

namespace TechHub.SSO.Tests;

public class AuthServiceTests : IDisposable
{
    private readonly SsoDbContext _context;
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        var options = new DbContextOptionsBuilder<SsoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _context = new SsoDbContext(options);
        _service = new AuthService(_context, new AuthCache(new MemoryCache(new MemoryCacheOptions())));
    }

    public void Dispose() => _context.Dispose();

    private async Task<(Tenant tenant, ApplicationUser user)> SeedUserAsync(string password = "Passw0rd!")
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Name = "Acme", Domain = "acme.example.com",
            IsActive = true, CreatedAt = DateTime.UtcNow,
            TrialEndDate = DateTime.UtcNow.AddDays(7), PlanType = PlanType.Trial
        };
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), Email = "user@acme.example.com",
            PasswordHash = PasswordHasher.Hash(password),
            FirstName = "A", LastName = "B", TenantId = tenant.Id,
            Roles = "admin", CreatedAt = DateTime.UtcNow
        };
        _context.Tenants.Add(tenant);
        _context.ApplicationUsers.Add(user);
        await _context.SaveChangesAsync();
        return (tenant, user);
    }

    [Fact]
    public async Task Register_CreatesTenantAndAdminUser()
    {
        var result = await _service.RegisterAsync("New@Example.COM", "Passw0rd!", "N", "E", "NewCo", "newco.example.com");
        Assert.True(result.Succeeded);
        Assert.Equal("new@example.com", result.User!.Email); // normalizado a minúsculas
        Assert.Equal(PlanType.Trial, result.Tenant!.PlanType);
        Assert.NotNull(result.Tenant.TrialEndDate);
    }

    [Fact]
    public async Task Register_DuplicateDomain_IsRejected()
    {
        await _service.RegisterAsync("a@b.com", "Passw0rd!", "A", "B", "Co1", "dup.example.com");
        var second = await _service.RegisterAsync("c@d.com", "Passw0rd!", "C", "D", "Co2", "dup.example.com");
        Assert.Equal(AuthOutcome.DuplicateDomain, second.Outcome);
    }

    [Fact]
    public async Task ValidateCredentials_Works_And_ResetsCounters()
    {
        var (tenant, _) = await SeedUserAsync();
        var ok = await _service.ValidateCredentialsAsync("USER@acme.example.com", tenant.Id, "Passw0rd!");
        Assert.True(ok.Succeeded);
        var stored = await _context.ApplicationUsers.FirstAsync(u => u.TenantId == tenant.Id);
        Assert.Equal(0, stored.AccessFailedCount);
        Assert.NotNull(stored.LastLoginAt);
    }

    [Fact]
    public async Task ValidateCredentials_SameEmail_DifferentTenant_IsScoped()
    {
        var (t1, _) = await SeedUserAsync();
        var t2 = new Tenant { Id = Guid.NewGuid(), Name = "Other", Domain = "other.example.com", IsActive = true, CreatedAt = DateTime.UtcNow, PlanType = PlanType.Basic };
        _context.Tenants.Add(t2);
        await _context.SaveChangesAsync();

        // El mismo email en otro tenant NO debe autenticar.
        var fail = await _service.ValidateCredentialsAsync("user@acme.example.com", t2.Id, "Passw0rd!");
        Assert.Equal(AuthOutcome.InvalidCredentials, fail.Outcome);
    }

    [Fact]
    public async Task ValidateCredentials_LocksOut_AfterFiveFailures()
    {
        var (tenant, _) = await SeedUserAsync();
        for (var i = 0; i < 5; i++)
            await _service.ValidateCredentialsAsync("user@acme.example.com", tenant.Id, "bad-pass");

        var locked = await _service.ValidateCredentialsAsync("user@acme.example.com", tenant.Id, "Passw0rd!");
        Assert.Equal(AuthOutcome.LockedOut, locked.Outcome);
    }

    [Fact]
    public async Task ValidateCredentials_TrialExpired_IsReported()
    {
        var (tenant, _) = await SeedUserAsync();
        tenant.TrialEndDate = DateTime.UtcNow.AddDays(-1);
        await _context.SaveChangesAsync();

        var result = await _service.ValidateCredentialsAsync("user@acme.example.com", tenant.Id, "Passw0rd!");
        Assert.Equal(AuthOutcome.TrialExpired, result.Outcome);
    }

    [Fact]
    public async Task Upgrade_ReactivatesAndClearsTrial()
    {
        var (tenant, _) = await SeedUserAsync();
        tenant.TrialEndDate = DateTime.UtcNow.AddDays(-1);
        tenant.IsActive = false;
        await _context.SaveChangesAsync();

        Assert.True(await _service.UpgradeTenantAsync(tenant.Id, PlanType.Pro));
        var updated = await _context.Tenants.FirstAsync(t => t.Id == tenant.Id);
        Assert.Equal(PlanType.Pro, updated.PlanType);
        Assert.Null(updated.TrialEndDate);
        Assert.True(updated.IsActive);
    }

    [Fact]
    public async Task IsTrialExpired_OnlyForTrialPlan()
    {
        var (tenant, _) = await SeedUserAsync();
        tenant.TrialEndDate = DateTime.UtcNow.AddDays(-1);
        await _context.SaveChangesAsync();
        Assert.True(await _service.IsTrialExpiredAsync(tenant.Id));

        tenant.PlanType = PlanType.Basic;
        await _context.SaveChangesAsync();
        Assert.False(await _service.IsTrialExpiredAsync(tenant.Id));
    }
}
