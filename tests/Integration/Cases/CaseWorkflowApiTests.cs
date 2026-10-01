namespace Aegis.Tests.Integration.Cases;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Shared.Security;
using Aegis.Tests.Integration.Alerts;

public sealed class CaseWorkflowApiTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    private async Task<Guid> CreateCaseAsync(Guid alertId)
    {
        var created = await _fx.AdminClient.PostAsync($"/api/v1/alerts/{alertId}/create-case", null);
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Linking_an_alert_adds_it_once_and_rejects_unknown_alerts()
    {
        var caseId = await CreateCaseAsync(await _fx.CreateStructuringAlertAsync());
        var second = await _fx.SeedAlertAsync();

        var link = await _fx.AdminClient.PostAsJsonAsync($"/api/v1/cases/{caseId}/alerts", new { alertId = second });
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        var body = await link.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(body.GetProperty("linkedAlertIds").EnumerateArray(), e => e.GetGuid() == second);

        var again = await _fx.AdminClient.PostAsJsonAsync($"/api/v1/cases/{caseId}/alerts", new { alertId = second });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(2, (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("linkedAlertIds").GetArrayLength());

        var unknown = await _fx.AdminClient.PostAsJsonAsync($"/api/v1/cases/{caseId}/alerts", new { alertId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Escalate_sets_status_and_closed_cases_return_conflict()
    {
        var caseId = await CreateCaseAsync(await _fx.CreateStructuringAlertAsync());

        var escalate = await _fx.AdminClient.PostAsJsonAsync($"/api/v1/cases/{caseId}/escalate", new { reason = "Needs MLRO review" });
        Assert.Equal(HttpStatusCode.OK, escalate.StatusCode);
        Assert.Equal("ESCALATED", (await escalate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var close = await _fx.AdminClient.PostAsJsonAsync($"/api/v1/cases/{caseId}/close",
            new { disposition = "FALSE_POSITIVE", conclusion = "Explained by payroll" });
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);

        var afterClose = await _fx.AdminClient.PostAsJsonAsync($"/api/v1/cases/{caseId}/escalate", new { });
        Assert.Equal(HttpStatusCode.Conflict, afterClose.StatusCode);
    }

    [Fact]
    public async Task Timeline_shows_alert_and_case_history_with_note_text_in_order()
    {
        var alertId = await _fx.CreateStructuringAlertAsync();
        var caseId = await CreateCaseAsync(alertId);
        (await _fx.AdminClient.PostAsJsonAsync($"/api/v1/cases/{caseId}/notes", new { text = "Called the customer" })).EnsureSuccessStatusCode();
        (await _fx.AdminClient.PostAsJsonAsync($"/api/v1/cases/{caseId}/escalate", new { })).EnsureSuccessStatusCode();
        var viewer = await _fx.CreateUserClientAsync($"viewer@{_fx.Slug}.test", new[] { RoleNames.Viewer });

        var response = await viewer.GetAsync($"/api/v1/cases/{caseId}/timeline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entries = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        var types = entries.Select(e => e.GetProperty("eventType").GetString()).ToList();
        Assert.Equal(new[] { "ALERT_CREATED", "CASE_CREATED", "CASE_NOTE_ADDED", "CASE_ESCALATED" }, types);
        var note = entries[2];
        Assert.Equal("NOTE", note.GetProperty("kind").GetString());
        Assert.Equal("Called the customer", note.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Case_assignee_must_be_an_active_tenant_user()
    {
        var caseId = await CreateCaseAsync(await _fx.CreateStructuringAlertAsync());

        var freeText = await _fx.AdminClient.PostAsJsonAsync($"/api/v1/cases/{caseId}/assign", new { assignedTo = "bob" });
        Assert.Equal(HttpStatusCode.BadRequest, freeText.StatusCode);

        var self = await _fx.AdminClient.PostAsJsonAsync($"/api/v1/cases/{caseId}/assign", new { });
        Assert.Equal(HttpStatusCode.OK, self.StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_link_or_escalate()
    {
        var caseId = await CreateCaseAsync(await _fx.CreateStructuringAlertAsync());
        var viewer = await _fx.CreateUserClientAsync($"viewer@{_fx.Slug}.test", new[] { RoleNames.Viewer });

        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PostAsJsonAsync($"/api/v1/cases/{caseId}/alerts", new { alertId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PostAsJsonAsync($"/api/v1/cases/{caseId}/escalate", new { })).StatusCode);
    }
}
