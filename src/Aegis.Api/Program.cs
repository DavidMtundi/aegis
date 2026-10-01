using System.Text;
using System.Threading.RateLimiting;
using Aegis.Api.Authorization;
using Aegis.Api.Middleware;
using Aegis.Infrastructure;
using Aegis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Aegis API");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    EnsureJwtSigningKeyConfigured(builder);

    builder.Services.AddControllers()
        .AddJsonOptions(o =>
        {
            o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new()
        {
            Title = "Aegis Financial Crime Compliance API",
            Version = "v1",
            Description = "Multi-tenant AML, KYC/KYB, screening, risk and case management platform."
        });
    });

    builder.Services.AddAegisInfrastructure(builder.Configuration);
    builder.Services.AddPermissionAuthorization();
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    builder.Services.AddCors(o => o.AddPolicy("Default", p =>
        p.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader()));

    var loginPermitLimit = builder.Configuration.GetValue("RateLimiting:Login:PermitLimit", 10);
    var loginWindow = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:Login:WindowSeconds", 60));
    builder.Services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = loginPermitLimit,
                Window = loginWindow,
                QueueLimit = 0
            }));
    });

    var app = builder.Build();

    if (app.Configuration.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AegisDbContext>();
        db.Database.Migrate();
    }

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Aegis API v1");
            c.RoutePrefix = string.Empty;
        });
    }

    app.UseCors("Default");
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseMiddleware<TenantContextMiddleware>();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks("/health");

    app.MapGet("/api/v1/status", (Aegis.Shared.Security.ITenantContext tenantContext) => new
    {
        status = "healthy",
        version = "1.0.0",
        platform = "Aegis Financial Crime Compliance",
        authenticated = tenantContext.IsAuthenticated,
        tenantId = tenantContext.IsAuthenticated ? tenantContext.TenantId.Value : (Guid?)null,
        timestamp = DateTimeOffset.UtcNow
    }).RequireAuthorization().WithName("GetStatus").WithTags("System");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Aegis API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static void EnsureJwtSigningKeyConfigured(WebApplicationBuilder builder)
{
    const string developmentDefault = "dev-only-signing-key-change-me-32chars-min!!";

    if (builder.Environment.IsDevelopment()
        && string.IsNullOrWhiteSpace(builder.Configuration["Jwt:SigningKey"]))
    {
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = developmentDefault
        });
    }

    var signingKey = builder.Configuration["Jwt:SigningKey"];
    if (string.IsNullOrWhiteSpace(signingKey))
    {
        throw new InvalidOperationException(
            "Jwt:SigningKey must be configured (set Jwt__SigningKey).");
    }

    if (!builder.Environment.IsDevelopment()
        && string.Equals(signingKey, developmentDefault, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "Jwt:SigningKey must not use the development default outside Development.");
    }
}

public partial class Program;
