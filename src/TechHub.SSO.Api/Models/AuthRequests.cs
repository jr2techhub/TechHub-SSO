using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Text.Json;
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

    // Solo planes de pago (Basic/Pro/Enterprise); un valor inválido produce 400.
    [Required]
    public PayPlanType NewPlan { get; set; }
}

/// <summary>
/// Endurecimiento: el plan no puede venir como valor libre del enum (p. ej. un número
/// inexistente o "Trial" para revertir una suscripción de pago). Solo se permiten
/// planes de pago explícitos, recibidos como string y validados aquí.
/// </summary>
[JsonConverter(typeof(PayPlanTypeJsonConverter))]
public enum PayPlanType
{
    Basic = 1,
    Pro = 2,
    Enterprise = 3
}

public sealed class PayPlanTypeJsonConverter : JsonConverter<PayPlanType>
{
    public override PayPlanType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String ||
            !Enum.TryParse<PayPlanType>(reader.GetString(), ignoreCase: true, out var value) ||
            !Enum.IsDefined(value))
        {
            throw new JsonException("Nuevo plan inválido: se esperaba Basic, Pro o Enterprise.");
        }
        return value;
    }

    public override void Write(Utf8JsonWriter writer, PayPlanType value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
