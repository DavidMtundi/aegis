namespace Aegis.Tests.Integration.Alerts;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Shared.Security;
using Microsoft.Extensions.DependencyInjection;

public sealed class AlertWorkflowApiTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task Dismiss_requires_reason_and_closed_alerts_reject_more_work()
    {
        var alertId = await _fx.CreateStructuringAlertAsync();

        var noReason = await _fx.AdminClient.PostAsJsonAsync($"/api/v1/alerts/{alertId}/dismiss", new { });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var dismiss = await _fx.AdminClient.PostAsJsonAsync(
            $"/api/v1/alerts/{alertId}/dismiss", new { reason = "Salary batch, verified with employer" });
        Assert.Equal(HttpStatusCode.OK, dismiss.StatusCode);
        var body = await dismiss.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DISMISSED", body.GetProperty("status").GetString());
        Assert.Equal("Salary batch, verified with employer", body.GetProperty("dismissalReason").GetString());

        var again = await _fx.AdminClient.PostAsJsonAsync($"/api/v1/alerts/{alertId}/resolve", new { });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Escalate_sets_status_and_is_audited()
    {
        var alertId = await _fx.CreateStructuringAlertAsync();

        var escalate = await _fx.AdminClient.PostAsJsonAsync(
            $"/api/v1/alerts/{alertId}/escalate", new { reason = "Linked to a sanctions hit" });

        Assert.Equal(HttpStatusCode.OK, escalate.StatusCode);
        var body = await escalate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ESCALATED", body.GetProperty("status").GetString());

        await using var scope = _fx.Factory.Services.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventRepository>();
        var events = await audit.ListByTenantAsync(_fx.TenantId, take: 50);
        var escalated = events.Single(e => e.EventType == AuditEventTypes.ALERT_ESCALATED);
        using var doc = JsonDocument.Parse(escalated.AfterState!);
        Assert.Equal("Linked to a sanctions hit", doc.RootElement.GetProperty("reason").GetString());

        var twice = await _fx.AdminClient.PostAsJsonAsync($"/api/v1/alerts/{alertId}/escalate", new { });
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_escalate()
    {
        var alertId = await _fx.CreateStructuringAlertAsync();
        var viewer = await _fx.CreateUserClientAsync($"viewer@{_fx.Slug}.test", new[] { RoleNames.Viewer });

        var response = await viewer.PostAsJsonAsync($"/api/v1/alerts/{alertId}/escalate", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Assignee_must_be_an_active_user_in_the_tenant()
    {
        var alertId = await _fx.CreateStructuringAlertAsync();
        await _fx.CreateUserClientAsync($"analyst@{_fx.Slug}.test", new[] { RoleNames.Analyst });
        var analystId = await FindUserIdAsync($"analyst@{_fx.Slug}.test");

        var freeText = await _fx.AdminClient.PostAsJsonAsync(
            $"/api/v1/alerts/{alertId}/assign", new { assignedTo = "bob" });
        Assert.Equal(HttpStatusCode.BadRequest, freeText.StatusCode);

        var unknown = await _fx.AdminClient.PostAsJsonAsync(
            $"/api/v1/alerts/{alertId}/assign", new { assignedTo = Guid.NewGuid().ToString() });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var ok = await _fx.AdminClient.PostAsJsonAsync(
            $"/api/v1/alerts/{alertId}/assign", new { assignedTo = analystId.ToString() });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(analystId.ToString(), (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("assignedTo").GetString());

        var deactivate = await _fx.AdminClient.PostAsync($"/api/v1/users/{analystId}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        var inactive = await _fx.AdminClient.PostAsJsonAsync(
            $"/api/v1/alerts/{alertId}/assign", new { assignedTo = analystId.ToString() });
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
    }

    private async Task<Guid> FindUserIdAsync(string email)
    {
        var users = await _fx.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/users");
        return users.EnumerateArray()
            .Single(u => u.GetProperty("email").GetString() == email)
            .GetProperty("id").GetGuid();
    }
}
