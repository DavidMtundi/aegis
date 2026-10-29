namespace Aegis.Api.Observability;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

public static class OpenTelemetryExtensions
{
    public static WebApplicationBuilder AddAegisOpenTelemetry(this WebApplicationBuilder builder)
    {
        var enabled = builder.Configuration.GetValue("OpenTelemetry:Enabled", true);
        if (!enabled)
            return builder;

        var serviceName = builder.Configuration["OpenTelemetry:ServiceName"] ?? "aegis-api";
        var consoleExporter = builder.Configuration.GetValue(
            "OpenTelemetry:ConsoleExporter",
            builder.Environment.IsDevelopment());

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName: serviceName, serviceVersion: "1.0.0"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(o =>
                    {
                        o.RecordException = true;
                        o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation();

                if (consoleExporter)
                    tracing.AddConsoleExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (consoleExporter)
                    metrics.AddConsoleExporter();
            });

        return builder;
    }
}
