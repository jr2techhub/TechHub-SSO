using Microsoft.AspNetCore.Authorization;

namespace TechHub.SSO.Api.Security;

/// <summary>
/// Exige un access token de OpenIddict (validado por el middleware de validación local).
/// Equivale a [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)].
/// </summary>
public sealed class AuthorizeClientAttribute : AuthorizeAttribute
{
    public AuthorizeClientAttribute()
    {
        AuthenticationSchemes = OpenIddict.Validation.AspNetCore
            .OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    }
}
