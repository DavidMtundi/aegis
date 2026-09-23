using System.Text;
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

    builder.Services.AddControllers();
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
    builder.Services.AddAuthorization();
    builder.Services.AddCors(o => o.AddPolicy("Default", p =>
        p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
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

public partial class Program;
