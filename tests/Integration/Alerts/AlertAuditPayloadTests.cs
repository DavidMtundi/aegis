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
    public async Task Assigning_with_quotes_in_name_succeeds_and_audits_exact_value()
    {
        var alertId = await _fx.CreateStructuringAlertAsync();
        var hostile = "bob\",\"isAdmin\":true,\"x\":\"";

        var response = await _fx.AdminClient.PostAsJsonAsync(
            $"/api/v1/alerts/{alertId}/assign", new { assignedTo = hostile });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _fx.Factory.Services.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventRepository>();
        var events = await audit.ListByTenantAsync(_fx.TenantId, take: 50);
        var assigned = events.First(e => e.EventType == AuditEventTypes.ALERT_ASSIGNED);
        using var doc = JsonDocument.Parse(assigned.AfterState!);
        Assert.Equal(hostile, doc.RootElement.GetProperty("assignedTo").GetString());
        Assert.False(doc.RootElement.TryGetProperty("isAdmin", out _));
    }
}
