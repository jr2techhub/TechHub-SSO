using System.ComponentModel.DataAnnotations;
using TechHub.SSO.Core.Entities;

namespace TechHub.SSO.Api.Models;

public class RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres"), MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string TenantName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Domain { get; set; } = string.Empty;
}

public class LoginRequest
{
    [Required]
    public Guid TenantId { get; set; }

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; set; } = string.Empty;
}

public class UpgradeRequest
{
    [Required]
    public Guid TenantId { get; set; }

    [Required]
    public PlanType NewPlan { get; set; }
}
