using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TechHub.SSO.Api.Data;

/// <summary>
/// Factoría usada por `dotnet ef` en tiempo de diseño para generar migraciones.
/// El provider se selecciona con la variable de entorno SSO_DB_PROVIDER
/// (por defecto Postgres). Las migraciones generadas son específicas del provider,
/// por lo que para MSSQL se deben regenerar una vez (ver README).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SsoDbContext>
{
    public SsoDbContext CreateDbContext(string[] args)
    {
        var provider = Environment.GetEnvironmentVariable("SSO_DB_PROVIDER") ?? "Postgres";
        var connectionString = provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
            ? "Server=localhost,1433;Database=TechHubIdentityDB;User Id=sa;Password=CHANGE_ME;TrustServerCertificate=True;"
            : "Host=localhost;Port=5432;Database=techhub_sso;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<SsoDbContext>();
        if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            optionsBuilder.UseSqlServer(connectionString);
        else
            optionsBuilder.UseNpgsql(connectionString);

        return new SsoDbContext(optionsBuilder.Options);
    }
}
