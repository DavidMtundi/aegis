namespace Aegis.Tests.Integration.Alerts;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Microsoft.Extensions.DependencyInjection;

public sealed class AlertAuditPayloadTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task Dismissing_with_quotes_in_reason_succeeds_and_audits_exact_value()
    {
        var alertId = await _fx.CreateStructuringAlertAsync();
        var hostile = "bob\",\"isAdmin\":true,\"x\":\"";

        var response = await _fx.AdminClient.PostAsJsonAsync(
            $"/api/v1/alerts/{alertId}/dismiss", new { reason = hostile });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _fx.Factory.Services.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventRepository>();
        var events = await audit.ListByTenantAsync(_fx.TenantId, take: 50);
        var dismissed = events.First(e => e.EventType == AuditEventTypes.ALERT_DISMISSED);
        using var doc = JsonDocument.Parse(dismissed.AfterState!);
        Assert.Equal(hostile, doc.RootElement.GetProperty("reason").GetString());
        Assert.False(doc.RootElement.TryGetProperty("isAdmin", out _));
    }
}
