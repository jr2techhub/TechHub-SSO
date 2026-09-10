using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TechHub.SSO.Api.Data;
using TechHub.SSO.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database
builder.Services.AddDbContext<SsoDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// OpenIddict configuration
builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<SsoDbContext>();
    })
    .AddServer(options =>
    {
        options.AllowAuthorizationCodeFlow()
               .AllowRefreshTokenFlow();

        options.SetAuthorizationEndpointUris("connect/authorize")
               .SetTokenEndpointUris("connect/token")
               .SetUserinfoEndpointUris("connect/userinfo")
               .SetLogoutEndpointUris("connect/logout");

        options.RegisterScopes("openid", "email", "profile", "roles", "erp_access", "scraper_access");

        options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough()
               .EnableTokenEndpointPassthrough()
               .EnableUserinfoEndpointPassthrough()
               .EnableLogoutEndpointPassthrough();

#if DEBUG
        options.AddDevelopmentEncryptionCertificate()
               .AddDevelopmentSigningCertificate();
#else
        // En producción usar certificados reales
        options.AddEphemeralEncryptionCertificate()
               .AddEphemeralSigningCertificate();
#endif
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

// Auth Service
builder.Services.AddScoped<IAuthService, AuthService>();

// CORS para permitir clientes desde diferentes orígenes
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Seed database inicial
await SeedDatabaseAsync(app.Services);

app.Run();

async Task SeedDatabaseAsync(IServiceProvider serviceProvider)
{
    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<SsoDbContext>();
    
    // Asegurar que la BD esté creada
    await context.Database.EnsureCreatedAsync();
    
    // Verificar si ya hay tenants
    if (!context.Tenants.Any())
    {
        // Crear tenant de demostración
        var demoTenant = new Core.Entities.Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Demo Tenant",
            Domain = "demo.techhub.com",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            TrialEndDate = DateTime.UtcNow.AddDays(7),
            PlanType = "Trial"
        };
        
        context.Tenants.Add(demoTenant);
        await context.SaveChangesAsync();
    }
}
