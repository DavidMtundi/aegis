namespace Aegis.Api.Middleware;

using System.Diagnostics;
using Aegis.Shared.Observability;
using Serilog.Context;

/// <summary>
/// Propagates a correlation id into TraceIdentifier, response headers, logs, and the current Activity.
/// Controllers already pass <see cref="HttpContext.TraceIdentifier"/> into audit events.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = CorrelationIds.HeaderName;
    public const string AlternateHeaderName = CorrelationIds.AlternateHeaderName;

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = CorrelationIds.FirstHeader(
            context.Request.Headers[HeaderName].FirstOrDefault(),
            context.Request.Headers[AlternateHeaderName].FirstOrDefault());
        var correlationId = CorrelationIds.Normalize(incoming, context.TraceIdentifier);

        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        Activity.Current?.SetTag("aegis.correlation_id", correlationId);

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
