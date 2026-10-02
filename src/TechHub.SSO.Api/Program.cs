using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using TechHub.SSO.Api.Data;
using TechHub.SSO.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Database ────────────────────────────────────────────────────────────────
// Backend por defecto: PostgreSQL. MSSQL preparado mediante "Database:Provider": "SqlServer".
builder.Services.AddDbContext<SsoDbContext>(options =>
    options.UseConfiguredProvider(builder.Configuration));

// ── OpenIddict ───────────────────────────────────────────────────────────────
builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<SsoDbContext>();
    })
    .AddServer(options =>
    {
        options.AllowAuthorizationCodeFlow()
               .AllowRefreshTokenFlow()
               .AllowClientCredentialsFlow();

        options.SetAuthorizationEndpointUris("connect/authorize")
               .SetTokenEndpointUris("connect/token")
               .SetUserinfoEndpointUris("connect/userinfo")
               .SetLogoutEndpointUris("connect/logout");

        options.RegisterScopes("openid", "email", "profile", "roles", "erp_access", "scraper_access");

        // Los clientes (techhub-web, techhub-service) se siembran en SsoDbSeeder.
        options.DisableAccessTokenEncryption(); // JWT legibles para recursos de terceros.

        options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough()
               .EnableTokenEndpointPassthrough()
               .EnableUserinfoEndpointPassthrough()
               .EnableLogoutEndpointPassthrough();

#if DEBUG
        // En desarrollo se permite HTTP para poder probar el flujo completo localmente.
        // En producción esto NUNCA se habilita: OpenIddict exige HTTPS por defecto.
        options.UseAspNetCore().DisableTransportSecurityRequirement();

        options.AddDevelopmentEncryptionCertificate()
               .AddDevelopmentSigningCertificate();
#else
        // Producción: certificados reales desde disco/almacén, NO efímeros
        // (los efímeros invalidan todos los tokens emitidos en cada reinicio).
        var certPath = builder.Configuration["OpenIddict:SigningCertificate:Path"];
        var certPassword = builder.Configuration["OpenIddict:SigningCertificate:Password"];
        if (!string.IsNullOrEmpty(certPath))
        {
            options.AddSigningCertificate(System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPemFile(
                certPath, certPassword));
        }
        else
        {
            throw new InvalidOperationException(
                "Falta OpenIddict:SigningCertificate:Path. No usar certificados efímeros en producción.");
        }
#endif
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

// ── Cookie de sesión para el flujo authorization_code ────────────────────────
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name = "techhub_sso_session";
        options.Cookie.HttpOnly = true;
#if DEBUG
        // En desarrollo (HTTP local) AllowOnlySecureCookies permitiría la cookie;
        // Always la descartaría silenciosamente en el navegador y rompería el SSO.
        // En producción se mantiene Always: la cookie solo viaja por HTTPS.
        options.Cookie.SecurePolicy = CookieSecurePolicy.None;
#else
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
#endif
        // Anti-CSRF: SameSite=Lax es el valor correcto para flujos OIDC redirect-based
        // (la cookie viaja en GET de redirección pero NO en POSTs cross-site).
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

// ── Caché (rendimiento) ──────────────────────────────────────────────────────
// Capa L1: memory cache siempre disponible.
builder.Services.AddMemoryCache();
// Capa L2 opcional: Redis distribuido si se define "Redis:ConnectionString".
var redisConnection = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrEmpty(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = "techhub-sso:";
    });
}

builder.Services.AddAuthorization();

// ── Aplicación y servicios ───────────────────────────────────────────────────
// El formulario de autenticación VIVE en este servidor (patrón Hosted Login /
// Authorization Server UI): los clientes OIDC solo hacen challenge-auth con
// returnurl; nunca reciben ni procesan credenciales.
builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<IAuthService, AuthService>();
// Caché de dos capas usada por AuthService (L1 memory siempre; L2 Redis opcional).
builder.Services.AddSingleton<IAuthCache>(sp =>
    new AuthCache(
        sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
        sp.GetService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>()));

// ── Rate limiting para endpoints de autenticación ────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,                    // máx. 10 peticiones...
                Window = TimeSpan.FromMinutes(1),    // ...por minuto y por IP origen
                AutoReplenishment = true
            }));
});

// ── CORS: lista blanca configurable (nunca AllowAnyOrigin con credenciales) ──
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? ["https://localhost:5001", "https://app.techhub.com"];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .WithMethods("GET", "POST")
              .WithHeaders("Authorization", "Content-Type");
    });
});

var app = builder.Build();

// ── Pipeline HTTP ────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Seed de base de datos (migraciones + tenant demo + clientes OpenIddict)
await SsoDbSeeder.SeedAsync(app.Services,
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SsoDbSeeder"));

app.Run();
