namespace Aegis.Tests.Integration.Alerts;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Shared.Security;

public sealed class AlertWorkRbacTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    [Theory]
    [InlineData("assign")]
    [InlineData("dismiss")]
    [InlineData("resolve")]
    [InlineData("create-case")]
    public async Task User_without_work_role_is_forbidden(string action)
    {
        var alertId = await _fx.CreateStructuringAlertAsync();
        var noRole = await _fx.CreateUserClientAsync($"viewer@{_fx.Slug}.test", Array.Empty<string>());

        var response = await noRole.PostAsJsonAsync($"/api/v1/alerts/{alertId}/{action}", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Analyst_can_work_alerts_and_cases()
    {
        var alertId = await _fx.CreateStructuringAlertAsync();
        var analyst = await _fx.CreateUserClientAsync($"analyst@{_fx.Slug}.test", new[] { RoleNames.Analyst });

        var created = await analyst.PostAsync($"/api/v1/alerts/{alertId}/create-case", null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var caseId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var note = await analyst.PostAsJsonAsync($"/api/v1/cases/{caseId}/notes", new { text = "Reviewed." });
        Assert.Equal(HttpStatusCode.OK, note.StatusCode);
    }

    [Fact]
    public async Task User_without_work_role_cannot_assign_or_annotate_cases()
    {
        var alertId = await _fx.CreateStructuringAlertAsync();
        var created = await _fx.AdminClient.PostAsync($"/api/v1/alerts/{alertId}/create-case", null);
        created.EnsureSuccessStatusCode();
        var caseId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var noRole = await _fx.CreateUserClientAsync($"viewer@{_fx.Slug}.test", Array.Empty<string>());

        var assign = await noRole.PostAsJsonAsync($"/api/v1/cases/{caseId}/assign", new { });
        var note = await noRole.PostAsJsonAsync($"/api/v1/cases/{caseId}/notes", new { text = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, assign.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, note.StatusCode);
    }
}
