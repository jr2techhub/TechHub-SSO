using System.ComponentModel.DataAnnotations;

namespace TechHub.SSO.Api.Models;

/// <summary>
/// Modelo del formulario hosted de registro (creación de tenant + usuario admin).
/// El registro crea el tenant con período de prueba y un único administrador.
/// </summary>
public class RegisterViewModel
{
    [Required, MaxLength(100)]
    [Display(Name = "Nombre de la organización")]
    public string TenantName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    [RegularExpression(@"^(?!-)[a-z0-9-]+(\.[a-z0-9-]+)*\.[a-z]{2,}$",
        ErrorMessage = "Introduzca un dominio válido (p. ej. miempresa.com).")]
    [Display(Name = "Dominio")]
    public string Domain { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    [Display(Name = "Nombre")]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    [Display(Name = "Apellidos")]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(256)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres."), MaxLength(128)]
    [DataType(DataType.Password)]
    [Display(Name = "Contraseña")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirmar contraseña")]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>Ruta local a la que volver tras un registro exitoso.</summary>
    public string? ReturnUrl { get; set; }
}
