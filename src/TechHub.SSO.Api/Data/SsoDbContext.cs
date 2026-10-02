using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore;
using TechHub.SSO.Core.Entities;

namespace TechHub.SSO.Api.Data;

public class SsoDbContext : DbContext
{
    public SsoDbContext(DbContextOptions<SsoDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Entidades de OpenIddict (Applications, Authorizations, Tokens, Scopes)
        builder.UseOpenIddict();

        // Configuración común compatible con PostgreSQL y SQL Server.
        // Se evitan tipos/columnas específicos de un proveedor para que el mismo
        // modelo funcione con ambos providers.
        builder.Entity<Tenant>(entity =>
        {
            entity.ToTable("tenants");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Domain).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Domain).IsUnique();
            // PlanType se persiste como string para legibilidad en ambas bases.
            entity.Property(e => e.PlanType)
                  .HasConversion<string>()
                  .HasMaxLength(32);
            entity.Property(e => e.CreatedAt).IsRequired();
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
            // Multi-tenancy: el mismo email puede existir en tenants distintos,
            // pero debe ser único DENTRO de cada tenant.
            entity.HasIndex(e => new { e.TenantId, e.Email }).IsUnique();
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.Roles).HasMaxLength(256);
            entity.HasOne(e => e.Tenant)
                  .WithMany(t => t.Users)
                  .HasForeignKey(e => e.TenantId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
