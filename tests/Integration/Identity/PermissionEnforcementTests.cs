namespace Aegis.Tests.Integration.Identity;

using System.Net;
using System.Net.Http.Json;
using Aegis.Shared.Security;
using Aegis.Tests.Integration.Alerts;

public sealed class PermissionEnforcementTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task Viewer_can_read_but_not_act()
    {
        var alertId = await _fx.CreateStructuringAlertAsync();
        var viewer = await _fx.CreateUserClientAsync($"viewer@{_fx.Slug}.test", new[] { RoleNames.Viewer });

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/alerts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/v1/alerts/{alertId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/rules")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/v1/alerts/{alertId}/dismiss", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/v1/customers", new
        {
            type = "INDIVIDUAL", country = "KE", firstName = "No", lastName = "Write"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/v1/audit-events")).StatusCode);
    }

    [Fact]
    public async Task Audit_log_needs_reviewer_or_admin()
    {
        var analyst = await _fx.CreateUserClientAsync($"analyst@{_fx.Slug}.test", new[] { RoleNames.Analyst });
        var reviewer = await _fx.CreateUserClientAsync($"reviewer@{_fx.Slug}.test", new[] { RoleNames.Reviewer });

        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.GetAsync("/api/v1/audit-events")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reviewer.GetAsync("/api/v1/audit-events")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _fx.AdminClient.GetAsync("/api/v1/audit-events")).StatusCode);
    }

    [Fact]
    public async Task User_without_roles_cannot_read_anything()
    {
        var none = await _fx.CreateUserClientAsync($"none@{_fx.Slug}.test", Array.Empty<string>());

        Assert.Equal(HttpStatusCode.Forbidden, (await none.GetAsync("/api/v1/alerts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await none.GetAsync("/api/v1/cases")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await none.GetAsync("/api/v1/transactions")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_get_401()
    {
        using var anonymous = _fx.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/alerts")).StatusCode);
    }
}
