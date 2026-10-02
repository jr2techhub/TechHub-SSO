using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TechHub.SSO.Api.Models;
using TechHub.SSO.Api.Security;
using TechHub.SSO.Api.Services;
using TechHub.SSO.Core.Entities;

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
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(
            request.Email, request.Password, request.FirstName,
            request.LastName, request.TenantName, request.Domain);

        return result.Outcome switch
        {
            AuthOutcome.Success => Ok(new
            {
                message = "Registro exitoso. Tu cuenta de prueba está activa por 7 días.",
                userId = result.User!.Id,
                tenantId = result.Tenant!.Id,
                trialEndsAt = result.Tenant.TrialEndDate
            }),
            AuthOutcome.DuplicateDomain => Conflict(new { message = "El dominio ya está registrado" }),
            _ => Conflict(new { message = "El email ya está registrado en este tenant" })
        };
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // Validación de credenciales scoped por tenant (multi-tenancy real).
        // La emisión de tokens OAuth se realiza vía POST /connect/token (OpenIddict).
        var result = await _authService.ValidateCredentialsAsync(
            request.Email, request.TenantId, request.Password);

        if (!result.Succeeded)
        {
            // Mensaje genérico: no revelar si el usuario existe (anti-enumeración).
            return Unauthorized(new { message = "Credenciales inválidas o acceso no permitido" });
        }

        var tenant = result.Tenant!;
        return Ok(new
        {
            message = "Login exitoso",
            email = result.User!.Email,
            tenantId = tenant.Id,
            isTrial = tenant.PlanType == PlanType.Trial,
            trialEndsAt = tenant.TrialEndDate
        });
    }

    [HttpPost("upgrade")]
    [AuthorizeClient]
    public async Task<IActionResult> Upgrade([FromBody] UpgradeRequest request)
    {
        // Protegido: solo un token de servicio válido puede cambiar planes.
        var success = await _authService.UpgradeTenantAsync(request.TenantId, (PlanType)request.NewPlan);

        if (!success)
            return BadRequest(new { message = "No se pudo actualizar el plan (tenant inexistente o plan no permitido)" });

        return Ok(new { message = "Plan actualizado exitosamente" });
    }
}
