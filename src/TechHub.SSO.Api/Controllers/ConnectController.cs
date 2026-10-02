using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using Microsoft.AspNetCore; // OpenIddictServerAspNetCoreHelpers (GetOpenIddictServerRequest)
using System.Collections.Immutable;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace TechHub.SSO.Api.Controllers;

/// <summary>
/// Endpoints OAuth2/OIDC de OpenIddict (habilitados vía passthrough en Program.cs).
/// Flujos soportados: authorization_code (+ refresh_token) para usuarios con sesión
/// en cookie, y client_credentials para servicios internos (p.ej. billing -> upgrade).
/// </summary>
public class ConnectController : Controller
{
    private readonly IOpenIddictApplicationManager _applicationManager;

    public ConnectController(IOpenIddictApplicationManager applicationManager)
    {
        _applicationManager = applicationManager;
    }

    // Requiere sesión iniciada (cookie). Si no la hay, el middleware redirige a LoginPath.
    [HttpGet("connect/authorize")]
    [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("La solicitud de autorización no es válida.");

        // La aplicación cliente debe existir (se siembra al arrancar la API).
        _ = await _applicationManager.FindByClientIdAsync(request.ClientId!)
            ?? throw new InvalidOperationException("La información de la aplicación es desconocida.");

        // Reconstruir la identidad desde la sesión de cookie del usuario.
        var identity = new ClaimsIdentity(
            authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            nameType: Claims.Name,
            roleType: Claims.Role);

        CopyClaim(User, identity, Claims.Subject);
        CopyClaim(User, identity, Claims.Email);
        CopyClaim(User, identity, Claims.GivenName);
        CopyClaim(User, identity, Claims.FamilyName);
        CopyClaim(User, identity, CustomClaims.TenantId);
        foreach (var role in User.FindAll(ClaimTypes.Role))
            AddDualClaim(identity, Claims.Role, role.Value);

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(request.GetScopes());
        principal.SetResources(ImmutableArray.Create("techhub-api"));

        foreach (var claim in principal.Claims)
            claim.SetDestinations(GetDestinations(claim));

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("connect/token"), Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("La solicitud de token no es válida.");

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            // Canje de code/refresh: la identidad viaja cifrada en el ticket de OpenIddict.
            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            if (result.Principal is null)
                throw new InvalidOperationException("No se pudo recuperar la identidad del ticket.");
            return SignIn(result.Principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsClientCredentialsGrantType())
        {
            // Flujo máquina-a-máquina para APIs internas (billing, automatizaciones).
            var application = await _applicationManager.FindByClientIdAsync(request.ClientId!)
                ?? throw new InvalidOperationException("La información de la aplicación es desconocida.");

            var identity = new ClaimsIdentity(
                authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                nameType: Claims.Name,
                roleType: Claims.Role);

            AddDualClaim(identity, Claims.Subject, await _applicationManager.GetIdAsync(application));
            AddDualClaim(identity, Claims.Name, await _applicationManager.GetDisplayNameAsync(application));
            AddDualClaim(identity, Claims.Role, "service");

            var principal = new ClaimsPrincipal(identity);
            principal.SetScopes(request.GetScopes());
            principal.SetResources(ImmutableArray.Create("techhub-api"));

            foreach (var claim in principal.Claims)
                claim.SetDestinations(GetDestinations(claim));

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new InvalidOperationException("El grant type especificado no está soportado.");
    }

    [HttpGet("connect/userinfo")]
    [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
    public IActionResult Userinfo()
    {
        static IEnumerable<string?> GetValues(ClaimsPrincipal principal, string type) =>
            principal.FindAll(type).Select(static claim => claim.Value);

        return Json(new Dictionary<string, object?>
        {
            ["sub"] = GetValues(User, Claims.Subject).SingleOrDefault(),
            ["email"] = GetValues(User, Claims.Email).SingleOrDefault(),
            ["given_name"] = GetValues(User, Claims.GivenName).SingleOrDefault(),
            ["family_name"] = GetValues(User, Claims.FamilyName).SingleOrDefault(),
            ["role"] = GetValues(User, Claims.Role),
            ["tenant_id"] = GetValues(User, CustomClaims.TenantId).SingleOrDefault()
        });
    }

    [HttpGet("connect/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { message = "Sesión cerrada" });
    }

    private static void CopyClaim(ClaimsPrincipal source, ClaimsIdentity target, string type)
    {
        var value = source.FindFirst(type)?.Value;
        if (!string.IsNullOrEmpty(value))
            AddDualClaim(target, type, value);
    }

    private static void AddDualClaim(ClaimsIdentity target, string type, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            target.AddClaim(new Claim(type, value));
            // Las claims sin destino explícito no se incluyen en ningún token;
            // los destinos se asignan en bloque sobre principal.Claims más abajo.
        }
    }

    /// <summary>Claims identificativos van a ambos tokens; el resto solo al access token.</summary>
    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        yield return Destinations.AccessToken;

        switch (claim.Type)
        {
            case Claims.Name:
            case Claims.Subject:
            case Claims.Role:
            case Claims.Email:
            case CustomClaims.TenantId:
                yield return Destinations.IdentityToken;
                break;
        }
    }
}

public static class CustomClaims
{
    public const string TenantId = "tenant_id";
}
