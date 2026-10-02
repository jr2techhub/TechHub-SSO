using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using TechHub.SSO.Core.Entities;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace TechHub.SSO.Api.Data;

/// <summary>
/// Siembra inicial de la base de datos: tenant demo + aplicación OpenIddict.
/// Idempotente: puede ejecutarse en cada arranque (usa migraciones aplicadas).
/// </summary>
public static class SsoDbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SsoDbContext>();

        // Migraciones incluidas: específicas de PostgreSQL (Migrations/Postgres).
        // Si usa SqlServer, genere primero sus migraciones propias (ver README).
        if (context.Database.IsRelational())
            await context.Database.MigrateAsync();

        await SeedTenantAsync(context, logger);
        await SeedApplicationsAsync(scope.ServiceProvider, logger);
    }

    private static async Task SeedTenantAsync(SsoDbContext context, ILogger logger)
    {
        if (await context.Tenants.AnyAsync())
            return;

        var demoTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Demo Tenant",
            Domain = "demo.techhub.com",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            TrialEndDate = DateTime.UtcNow.AddDays(7),
            PlanType = PlanType.Trial
        };

        context.Tenants.Add(demoTenant);
        await context.SaveChangesAsync();
        logger.LogInformation("Tenant demo sembrado: {Domain}", demoTenant.Domain);
    }

    private static async Task SeedApplicationsAsync(IServiceProvider services, ILogger logger)
    {
        var manager = services.GetRequiredService<IOpenIddictApplicationManager>();

        if (await manager.FindByClientIdAsync("techhub-web") is null)
        {
            // Cliente público SPA con flujo authorization code + PKCE.
            await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "techhub-web",
                ConsentType = ConsentTypes.Implicit,
                DisplayName = "TechHub Web Client",
                ClientType = ClientTypes.Public,
                RedirectUris =
                {
                    new Uri("https://localhost:5001/signin-oidc")
                },
                PostLogoutRedirectUris =
                {
                    new Uri("https://localhost:5001/signout-callback-oidc")
                },
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code,
                    Permissions.Scopes.Email,
                    Permissions.Scopes.Profile,
                    Permissions.Scopes.Roles,
                    Permissions.Prefixes.Scope + "erp_access",
                    Permissions.Prefixes.Scope + "scraper_access"
                }
            });
            logger.LogInformation("Aplicación OpenIddict 'techhub-web' creada.");
        }

        if (await manager.FindByClientIdAsync("techhub-service") is null)
        {
            // Cliente confidencial interno (M2M) para endpoints de administración como /api/auth/upgrade.
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = "techhub-service",
                ClientSecret = Environment.GetEnvironmentVariable("SSO_SERVICE_CLIENT_SECRET")
                    ?? "ChangeMe_Dev_Secret_123!", // Solo desarrollo; en producción definir la variable de entorno.
                ConsentType = ConsentTypes.Systematic,
                DisplayName = "TechHub Internal Service",
                ClientType = ClientTypes.Confidential,
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.Scopes.Roles,
                    Permissions.Prefixes.Scope + "erp_access"
                }
            };
            await manager.CreateAsync(descriptor);
            logger.LogInformation("Aplicación OpenIddict 'techhub-service' creada.");
        }
    }
}
