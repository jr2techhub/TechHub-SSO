# TechHub-SSO

Servidor de Identidad Centralizado (Single Sign-On - SSO) para el ecosistema TechHub.

## Descripción

Este proyecto implementa un servidor de identidad basado en **OpenIddict** y **.NET 10** que proporciona autenticación centralizada para todos los productos del ecosistema TechHub (ERP-Lite, MarketScraper, etc.).

## Características

- ✅ **OAuth2 / OpenID Connect** compatible
- ✅ **Flujo de Authorization Code** con PKCE
- ✅ **Gestión Multi-Tenant** con soporte para múltiples organizaciones
- ✅ **Trial Automático** de 7 días para nuevos registros
- ✅ **Bloqueo y Limpieza** automática post-trial
- ✅ **Escalabilidad** para agregar nuevos clientes (productos) fácilmente

## Estructura del Proyecto

```
TechHub-SSO/
├── src/
│   ├── TechHub.SSO.Api/          # API principal con OpenIddict
│   │   ├── Controllers/           # Controladores (Auth, Register)
│   │   ├── Data/                  # DbContext
│   │   ├── Services/              # Servicios de autenticación
│   │   └── Program.cs             # Configuración OpenIddict
│   └── TechHub.SSO.Core/          # Entidades compartidas
│       └── Entities/
└── TechHub-SSO.sln
```

## Configuración

### Base de Datos

El sistema usa **SQL Server**. Configura la cadena de conexión en `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost,1433;Database=TechHubIdentityDB;User Id=sa;Password=TuPassword;TrustServerCertificate=True;"
  }
}
```

### Ejecución

1. Restaurar paquetes:
   ```bash
   dotnet restore
   ```

2. Aplicar migraciones (si se usan):
   ```bash
   dotnet ef database update --project src/TechHub.SSO.Api
   ```

3. Ejecutar el servidor:
   ```bash
   dotnet run --project src/TechHub.SSO.Api
   ```

El servidor estará disponible en `https://localhost:5001` por defecto.

## Registro de Nuevos Tenants

Los nuevos usuarios pueden registrarse vía API:

```bash
POST https://localhost:5001/api/auth/register
Content-Type: application/json

{
  "email": "usuario@empresa.com",
  "password": "Password123!",
  "firstName": "Juan",
  "lastName": "Pérez",
  "tenantName": "Mi Empresa SAS",
  "domain": "miempresa.techhub.com"
}
```

**Respuesta:**
- Se crea un Tenant con plan **Trial** de 7 días
- El usuario puede acceder inmediatamente a todos los productos conectados
- Al vencer los 7 días, la cuenta se bloquea automáticamente

## Integración con Clientes (Productos)

Para integrar un producto como cliente OIDC:

### 1. Registrar el cliente en OpenIddict

En `Program.cs` del SSO, agregar:

```csharp
// Ejemplo para ERP-Lite
options.AddClient(clientId: "erplite-client",
    consentType: OpenIddictConstants.ConsentTypes.Implicit,
    permissions:
    {
        OpenIddictConstants.Permissions.Endpoints.Authorization,
        OpenIddictConstants.Permissions.Endpoints.Token,
        OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
        OpenIddictConstants.Permissions.ResponseTypes.Code,
        OpenIddictConstants.Permissions.Scopes.Email,
        OpenIddictConstants.Permissions.Scopes.Profile,
        OpenIddictConstants.Permissions.Prefixes.Scope + "erp_access"
    },
    redirectUris: new[] { "https://erplite.local/signin-oidc" },
    postLogoutRedirectUris: new[] { "https://erplite.local/signout-callback-oidc" });
```

### 2. Configurar el cliente

En el `appsettings.json` del producto cliente:

```json
{
  "SsoSettings": {
    "Authority": "https://localhost:5001",
    "ClientId": "erplite-client",
    "ClientSecret": "secret-opcional",
    "Scopes": ["openid", "profile", "email", "erp_access"]
  }
}
```

## Scopes Disponibles

- `openid` - Requerido para OIDC
- `email` - Acceso al email del usuario
- `profile` - Información básica del perfil
- `roles` - Roles del usuario
- `erp_access` - Acceso al ERP-Lite
- `scraper_access` - Acceso al MarketScraper

## Seguridad

- **Desarrollo**: Usa certificados de desarrollo auto-firmados
- **Producción**: Configurar certificados SSL reales en `Program.cs`
- **Passwords**: Hash con SHA256 (mejorar a BCrypt/Argon2 en producción)
- **CORS**: Configurado para permitir todos los orígenes en desarrollo (restringir en producción)

## Roadmap

- [ ] Implementar BCrypt para hashing de contraseñas
- [ ] Agregar MFA (Multi-Factor Authentication)
- [ ] Panel de administración de tenants
- [ ] Integración con Google/GitHub Login
- [ ] Auditoría de logs de acceso
- [ ] Soporte para SAML2 (empresas enterprise)

## Licencia

Propietario - TechHub © 2025

---

**Documentación bilingüe / Bilingual documentation:**

This project implements an Identity Server based on **OpenIddict** and **.NET 10** providing centralized authentication for all TechHub ecosystem products.

New users get a **7-day Trial** automatically. After expiration, accounts are blocked and data cleaned.

For production use, configure real SSL certificates and strengthen password hashing.
