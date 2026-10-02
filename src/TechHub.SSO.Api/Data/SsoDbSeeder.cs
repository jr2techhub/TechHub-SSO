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
        await SeedUsersAsync(context, logger);
        await SeedApplicationsAsync(scope.ServiceProvider, logger);
#if DEBUG
        // En desarrollo también se permite el callback local HTTP de la SPA (ver README).
        await EnsureDevRedirectUriAsync(scope.ServiceProvider, logger);
#endif
    }

#if DEBUG
    private static async Task EnsureDevRedirectUriAsync(IServiceProvider services, ILogger logger)
    {
        var manager = services.GetRequiredService<IOpenIddictApplicationManager>();
        if (await manager.FindByClientIdAsync("techhub-web") is not object app) return;

        var devUri = new Uri("http://localhost:3000/callback");
        var devLogoutUri = new Uri("http://localhost:3000/signout-callback-oidc");

        var existing = (await manager.GetRedirectUrisAsync(app)).ToList();
        var existingLogout = (await manager.GetPostLogoutRedirectUrisAsync(app)).ToList();
        if (!existing.Contains(devUri.ToString()))
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = "techhub-web",
                ConsentType = ConsentTypes.Implicit,
                DisplayName = "TechHub Web Client",
                ClientType = ClientTypes.Public,
            };
            foreach (var uri in existing.Select(u => new Uri(u)).Append(devUri))
                descriptor.RedirectUris.Add(uri);
            foreach (var uri in existingLogout.Select(u => new Uri(u)).Append(devLogoutUri))
                descriptor.PostLogoutRedirectUris.Add(uri);

            // UpdateAsync con descriptor reemplaza los valores: conservamos los existentes.
            await manager.UpdateAsync(app, descriptor);
            logger.LogInformation("Redirect URI de desarrollo añadido a techhub-web: {Uri}", devUri);
        }
    }
#endif

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

    /// <summary>
    /// Usuarios de prueba para el tenant demo (solo desarrollo).
    /// Contraseñas: Admin123! / User123! — NO usar estas credenciales en producción.
    /// </summary>
    private static async Task SeedUsersAsync(SsoDbContext context, ILogger logger)
    {
        if (await context.ApplicationUsers.AnyAsync())
            return;

        var tenant = await context.Tenants.FirstOrDefaultAsync();
        if (tenant is null) return;

        var users = new[]
        {
            new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = "admin@demo.techhub.com",
                PasswordHash = TechHub.SSO.Api.Services.PasswordHasher.Hash("Admin123!"),
                FirstName = "Ada",
                LastName = "Lovelace",
                TenantId = tenant.Id,
                IsEmailConfirmed = true,
                Roles = "admin",
                CreatedAt = DateTime.UtcNow
            },
            new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = "user@demo.techhub.com",
                PasswordHash = TechHub.SSO.Api.Services.PasswordHasher.Hash("User123!"),
                FirstName = "Alan",
                LastName = "Turing",
                TenantId = tenant.Id,
                IsEmailConfirmed = true,
                Roles = "user",
                CreatedAt = DateTime.UtcNow
            }
        };

        context.ApplicationUsers.AddRange(users);
        await context.SaveChangesAsync();
        logger.LogInformation("Usuarios de prueba sembrados: {Emails}",
            string.Join(", ", users.Select(u => u.Email)));
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
