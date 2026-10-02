using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TechHub.SSO.Api.Services;

namespace TechHub.SSO.Api.Controllers;

/// <summary>
/// Inicio de sesión por cookie que habilita el flujo authorization_code de OpenIddict.
/// Tras credenciales válidas se emite la cookie de sesión y se vuelve a la solicitud original.
/// </summary>
[ApiController]
[Route("account")]
public class AccountController : ControllerBase
{
    private readonly IAuthService _authService;

    public AccountController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromQuery] Guid tenantId, [FromForm] string email, [FromForm] string password)
    {
        var result = await _authService.ValidateCredentialsAsync(email, tenantId, password);
        if (!result.Succeeded)
            return Unauthorized(new { message = "Credenciales inválidas o acceso no permitido" });

        var user = result.User!;
        var tenant = result.Tenant!;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("sub", user.Id.ToString()),
            new("email", user.Email),
            new("given_name", user.FirstName),
            new("family_name", user.LastName),
            new("tenant_id", tenant.Id.ToString())
        };
        foreach (var role in user.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries))
            claims.Add(new Claim(ClaimTypes.Role, role.Trim()));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        return Ok(new { message = "Sesión iniciada. Redirija ahora a /connect/authorize." });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { message = "Sesión cerrada" });
    }
}
