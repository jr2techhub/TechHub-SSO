using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TechHub.SSO.Api.Models;
using TechHub.SSO.Api.Services;

namespace TechHub.SSO.Api.Controllers;

/// <summary>
/// UI de autenticación HOSTED: el formulario de login vive exclusivamente en este
/// servidor OAuth (patrón Authorization Server UI / Hosted Login Page).
/// Los clientes (SPA, APIs, UX del stack TechHub) NUNCA gestionan credenciales:
/// solo reciben un challenge-auth con returnurl y, tras la sesión en cookie,
/// OpenIddict emite el code por el canal redirect. El cliente jamás ve password.
/// </summary>
public class AccountController : Controller
{
    private readonly IAuthService _authService;

    public AccountController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>GET /account/login?returnurl=/connect/authorize?... — formulario hosted.</summary>
    [HttpGet("account/login")]
    public IActionResult Login(string? returnurl, Guid? tenantId, string? error = null)
    {
        // Solo se aceptan rutas locales como destino de retorno (anti open-redirect).
        if (returnurl is not null && !IsLocalReturnUrl(returnurl))
            returnurl = null;

        ViewData["Error"] = error switch
        {
            "invalid" => "Credenciales inválidas o acceso no permitido.",
            "lockedout" => "Cuenta bloqueada temporalmente por intentos fallidos. Inténtelo de nuevo en unos minutos.",
            "trial-expired" => "El período de prueba de este tenant ha expirado. Contacte a su administrador.",
            "tenant-disabled" => "Este tenant está desactivado.",
            _ => null
        };

        return View(new LoginViewModel { TenantId = tenantId ?? Guid.Empty, ReturnUrl = returnurl });
    }

    /// <summary>POST /account/login?returnurl=... — validación de credenciales + cookie de sesión SSO.</summary>
    [HttpPost("account/login")]
    [ValidateAntiForgeryToken] // Protegido por antiforgery: la página se sirve este servidor y la cookie SameSite=Lax viaja en el POST same-site.
    public async Task<IActionResult> Login(LoginViewModel model, string? returnurl = null)
    {
        returnurl ??= model.ReturnUrl;
        if (returnurl is not null && !IsLocalReturnUrl(returnurl))
            returnurl = null;

        if (!ModelState.IsValid)
        {
            ModelState.AddModelError(string.Empty, "Compruebe los datos introducidos.");
            model.ReturnUrl = returnurl;
            return View(model);
        }

        var result = await _authService.ValidateCredentialsAsync(model.Email, model.TenantId, model.Password);
        if (!result.Succeeded)
        {
            // Re-render hosted del formulario con mensaje genérico (no revela existencia de usuario).
            ViewData["Error"] = result.Outcome switch
            {
                AuthOutcome.LockedOut => "Cuenta bloqueada temporalmente por intentos fallidos. Inténtelo de nuevo en unos minutos.",
                AuthOutcome.TrialExpired => "El período de prueba de este tenant ha expirado. Contacte a su administrador.",
                AuthOutcome.TenantDisabled => "Este tenant está desactivado.",
                _ => "Credenciales inválidas o acceso no permitido."
            };
            model.Password = string.Empty;
            model.ReturnUrl = returnurl;
            return View(model);
        }

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

        // Volver a la solicitud de autorización original (server-side) para que
        // OpenIddict emita el authorization code hacia el redirect_uri del cliente.
        if (returnurl is not null)
            return Redirect(returnurl);

        // API host no confiable: devolver 204 y dejar que el navegador continúe el flujo.
        return NoContent();
    }

    /// <summary>GET /account/register — formulario hosted de alta (tenant + admin).</summary>
    [HttpGet("account/register")]
    public IActionResult Register(string? returnurl, string? error = null)
    {
        if (returnurl is not null && !IsLocalReturnUrl(returnurl))
            returnurl = null;

        ViewData["Error"] = error switch
        {
            "duplicate" => "Ya existe una organización con ese dominio o un usuario con ese email.",
            _ => null
        };

        return View(new RegisterViewModel { ReturnUrl = returnurl });
    }

    /// <summary>POST /account/register — crea tenant+admin y firma la sesión SSO directamente.</summary>
    [HttpPost("account/register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, string? returnurl = null)
    {
        returnurl ??= model.ReturnUrl;
        if (returnurl is not null && !IsLocalReturnUrl(returnurl))
            returnurl = null;

        if (!ModelState.IsValid)
        {
            ViewData["Error"] = "Compruebe los datos introducidos.";
            model.Password = model.ConfirmPassword = string.Empty;
            model.ReturnUrl = returnurl;
            return View(model);
        }

        var result = await _authService.RegisterAsync(
            model.Email, model.Password, model.FirstName, model.LastName,
            model.TenantName, model.Domain);

        if (!result.Succeeded)
        {
            // Mensaje genérico: no revelamos si el conflicto es el dominio o el email.
            ViewData["Error"] = "No se pudo completar el registro. Verifique que el dominio y el email no estén ya en uso.";
            model.ReturnUrl = returnurl;
            return View(model);
        }

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

        if (returnurl is not null)
            return Redirect(returnurl);

        // Sin returnurl: continuar hacia el login normal (sesión ya creada → prompt=none funciona).
        return RedirectToAction(nameof(Login));
    }

    [HttpPost("account/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Json(new { message = "Sesión cerrada" });
    }

    private static bool IsLocalReturnUrl(string url) =>
        url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\");
}
