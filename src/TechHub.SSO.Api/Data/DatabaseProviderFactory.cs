using Microsoft.EntityFrameworkCore;

namespace TechHub.SSO.Api.Data;

/// <summary>
/// Proveedores de base de datos soportados. PostgreSQL es el backend por defecto;
/// SQL Server (MSSQL) queda preparado y se activa mediante configuración.
/// </summary>
public static class DatabaseProviderFactory
{
    public const string Postgres = "Postgres";
    public const string SqlServer = "SqlServer";

    /// <summary>
    /// Configura el <see cref="DbContextOptionsBuilder"/> según la clave
    /// "Database:Provider" de la configuración (valores: "Postgres" | "SqlServer").
    /// Si no se especifica, usa PostgreSQL.
    /// </summary>
    public static DbContextOptionsBuilder UseConfiguredProvider(
        this DbContextOptionsBuilder optionsBuilder,
        IConfiguration configuration,
        string connectionStringName = "DefaultConnection")
    {
        var provider = configuration["Database:Provider"] ?? Postgres;
        var connectionString = configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException(
                $"No connection string found for '{connectionStringName}'.");

        switch (provider.Trim().ToLowerInvariant())
        {
            case "postgres":
            case "npgsql":
                optionsBuilder.UseNpgsql(connectionString, npgsql =>
                    npgsql.EnableRetryOnFailure(maxRetryCount: 5));
                break;

            case "sqlserver":
            case "mssql":
                // MSSQL preparado: requiere el paquete Microsoft.EntityFrameworkCore.SqlServer
                // y una cadena de conexión adecuada. Las migraciones incluidas funcionan en ambos.
                optionsBuilder.UseSqlServer(connectionString, sql =>
                    sql.EnableRetryOnFailure(maxRetryCount: 5));
                break;

            default:
                throw new InvalidOperationException(
                    $"Database provider '{provider}' no soportado. Usa '{Postgres}' o '{SqlServer}'.");
        }

        return optionsBuilder;
    }
}
