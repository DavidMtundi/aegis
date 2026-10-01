namespace Aegis.Tests.Integration.Customers;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Shared.Security;
using Aegis.Tests.Integration.Alerts;

public sealed class CustomerOverviewTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task Overview_returns_profile_accounts_transactions_alerts_and_cases()
    {
        var alertId = await _fx.CreateStructuringAlertAsync();
        var created = await _fx.AdminClient.PostAsync($"/api/v1/alerts/{alertId}/create-case", null);
        created.EnsureSuccessStatusCode();
        var viewer = await _fx.CreateUserClientAsync($"viewer@{_fx.Slug}.test", new[] { RoleNames.Viewer });

        var response = await viewer.GetAsync($"/api/v1/customers/{_fx.CustomerId}/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(_fx.CustomerId, body.GetProperty("customer").GetProperty("id").GetGuid());
        Assert.Equal(_fx.AccountId, body.GetProperty("accounts")[0].GetProperty("id").GetGuid());
        Assert.Equal(5, body.GetProperty("recentTransactions").GetArrayLength());
        Assert.Equal(alertId, body.GetProperty("alerts")[0].GetProperty("id").GetGuid());
        Assert.Equal(1, body.GetProperty("cases").GetArrayLength());

        var summary = body.GetProperty("summary");
        Assert.Equal(5, summary.GetProperty("transactionCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("openAlerts").GetInt32());
        Assert.Equal(1, summary.GetProperty("openCases").GetInt32());
    }

    [Fact]
    public async Task Overview_of_unknown_customer_is_404()
    {
        var response = await _fx.AdminClient.GetAsync($"/api/v1/customers/{Guid.NewGuid()}/overview");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
