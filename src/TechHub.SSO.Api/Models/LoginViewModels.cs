using System.ComponentModel.DataAnnotations;

namespace TechHub.SSO.Api.Models;

/// <summary>
/// Modelo del formulario hosted de login. El returnurl SOLO puede ser una ruta
/// local (empezar por '/'), lo que evita open redirects hacia dominios externos.
/// </summary>
public class LoginViewModel
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Tenant scoped: el usuario se autentica dentro de su tenant.</summary>
    [Required]
    public Guid TenantId { get; set; }

    /// <summary>Ruta local a la que volver tras un login exitoso (p. ej. /connect/authorize?...).</summary>
    public string? ReturnUrl { get; set; }

    public bool IsLocalRedirect() =>
        !string.IsNullOrEmpty(ReturnUrl) &&
        ReturnUrl.StartsWith('/') &&
        !ReturnUrl.StartsWith("//") &&
        !ReturnUrl.StartsWith("/\\");
}

/// <summary>Modelo del selector de tenant previo al formulario de credenciales.</summary>
public class TenantPickerViewModel
{
    [Required, MaxLength(256)]
    public string Domain { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}
