using System.Text;
using System.Threading.RateLimiting;
using BankPortal.Api.Health;
using BankPortal.Api.Middleware;
using BankPortal.Application.Auth;
using BankPortal.Infrastructure;
using BankPortal.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

// Only trust X-Forwarded-* when the API runs behind our own reverse proxy (nginx in Compose).
// The API port must not be reachable directly in that mode, otherwise clients could spoof their IP.
var behindProxy = builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled");
if (behindProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownNetworks.Clear();
        o.KnownProxies.Clear();
    });
}

// Auth endpoints are limited per client IP, so one noisy client cannot lock everyone else out.
var authPermitPerMinute = builder.Configuration.GetValue("RateLimit:AuthPermitPerMinute", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = authPermitPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>()
          ?? throw new InvalidOperationException("Missing Jwt configuration.");
if (jwt.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 32 characters.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;   // keep claim names as issued: "sub", "role"
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", p => p.RequireRole(Roles.Admin));

var app = builder.Build();

if (behindProxy) app.UseForwardedHeaders();     // must be first so the rate limiter sees the real client IP
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment()) app.MapOpenApi();

// Roles and the admin user: always in Development, in other environments only when Seed:OnStartup=true.
// Idempotent. A failure is logged and never stops the API from starting.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Seed:OnStartup"))
{
    try
    {
        using var scope = app.Services.CreateScope();
        await IdentitySeeder.SeedAsync(scope.ServiceProvider);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Seeding roles and the admin user failed.");
    }
}

if (!behindProxy) app.UseHttpsRedirection();    // behind the proxy, TLS ends at the proxy
app.UseRateLimiter();
app.UseAuthentication();     // must come before UseAuthorization
app.UseAuthorization();
app.MapControllers();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") });

app.Run();

// Makes the entry point visible to WebApplicationFactory in the integration tests.
public partial class Program { }