namespace TechHub.SSO.Core.Entities;

public enum PlanType
{
    Trial = 0,
    Basic = 1,
    Pro = 2,
    Enterprise = 3
}

public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? TrialEndDate { get; set; }
    public PlanType PlanType { get; set; } = PlanType.Trial;

    /// <summary>Indica si el trial del tenant ha expirado (solo aplica a PlanType.Trial).</summary>
    public bool IsTrialExpired(DateTime utcNow) =>
        PlanType == PlanType.Trial && TrialEndDate.HasValue && TrialEndDate.Value < utcNow;

    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
}

public class ApplicationUser
{
    public Guid Id { get; set; }

    /// <summary>Email normalizado (minúsculas). Único dentro de cada tenant.</summary>
    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public bool IsEmailConfirmed { get; set; }

    /// <summary>Roles de la cuenta (p.ej. "admin").</summary>
    public string Roles { get; set; } = string.Empty;

    /// <summary>Contador de intentos fallidos y bloqueo temporal anti-fuerza bruta.</summary>
    public int AccessFailedCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public Tenant? Tenant { get; set; }
}
