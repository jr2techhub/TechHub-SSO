using Microsoft.AspNetCore.Mvc;
using TechHub.SSO.Api.Services;

namespace TechHub.SSO.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var user = await _authService.RegisterAsync(
            request.Email, 
            request.Password, 
            request.FirstName, 
            request.LastName, 
            request.TenantName, 
            request.Domain);

        if (user == null)
            return BadRequest(new { message = "El email ya está registrado" });

        return Ok(new { 
            message = "Registro exitoso. Tu cuenta de prueba está activa por 7 días.",
            userId = user.Id,
            trialEndsAt = DateTime.UtcNow.AddDays(7)
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var isValid = await _authService.ValidateCredentialsAsync(request.Email, request.Password);
        
        if (!isValid)
            return Unauthorized(new { message = "Credenciales inválidas o cuenta expirada" });

        var user = await _authService.GetUserByEmailAsync(request.Email);
        var tenant = await GetTenantByUserId(user!.Id); // Método auxiliar necesario
        
        // Aquí se generarían los tokens OpenIddict
        // Por ahora retornamos información básica
        return Ok(new {
            message = "Login exitoso",
            email = user.Email,
            tenantId = user.TenantId,
            isTrial = tenant.PlanType == "Trial",
            trialEndsAt = tenant.TrialEndDate
        });
    }

    [HttpPost("upgrade")]
    public async Task<IActionResult> Upgrade([FromBody] UpgradeRequest request)
    {
        var success = await _authService.UpgradeTenantAsync(request.TenantId, request.NewPlan);
        
        if (!success)
            return BadRequest(new { message = "No se pudo actualizar el plan" });

        return Ok(new { message = "Plan actualizado exitosamente" });
    }

    private async Task<dynamic> GetTenantByUserId(Guid userId)
    {
        // Implementación simplificada - en producción usar inyección de DbContext
        throw new NotImplementedException();
    }
}

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class UpgradeRequest
{
    public Guid TenantId { get; set; }
    public string NewPlan { get; set; } = string.Empty;
}
