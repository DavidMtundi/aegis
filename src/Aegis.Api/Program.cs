using Serilog;

// ─── Logging bootstrap ────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Aegis API");

    var builder = WebApplication.CreateBuilder(args);

    // ─── Serilog ─────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    // ─── Core services ────────────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new()
        {
            Title    = "Aegis Financial Crime Compliance API",
            Version  = "v1",
            Description = "Multi-tenant AML, KYC/KYB, screening, risk and case management platform."
        });
    });

    // ─── Authentication ───────────────────────────────────────────────────────
    builder.Services.AddAuthentication()
        .AddJwtBearer(opts =>
        {
            opts.Authority = builder.Configuration["Jwt:Authority"];
            opts.Audience  = builder.Configuration["Jwt:Audience"];
        });

    builder.Services.AddAuthorization();

    // ─── CORS ─────────────────────────────────────────────────────────────────
    builder.Services.AddCors(o => o.AddPolicy("Default", p =>
        p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

    // ─── Health checks ────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks();

    // ─────────────────────────────────────────────────────────────────────────
    // TODO (Milestone 1): Register module services
    // builder.Services.AddIdentityModule(builder.Configuration);
    // builder.Services.AddTransactionsModule(builder.Configuration);
    // builder.Services.AddAmlModule(builder.Configuration);
    // builder.Services.AddAlertsModule(builder.Configuration);
    // builder.Services.AddCasesModule(builder.Configuration);
    // builder.Services.AddAuditModule(builder.Configuration);
    // ─────────────────────────────────────────────────────────────────────────

    var app = builder.Build();

    // ─── Middleware pipeline ──────────────────────────────────────────────────
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

    app.UseHttpsRedirection();
    app.UseCors("Default");
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks("/health");

    // ─── Versioned API base route ─────────────────────────────────────────────
    app.MapGet("/api/v1/status", () => new
    {
        status    = "healthy",
        version   = "1.0.0",
        platform  = "Aegis Financial Crime Compliance",
        timestamp = DateTimeOffset.UtcNow
    }).WithName("GetStatus").WithTags("System");

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
