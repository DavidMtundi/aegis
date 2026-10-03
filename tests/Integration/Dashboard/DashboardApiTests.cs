namespace Aegis.Tests.Integration.Dashboard;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Tests.Integration.Alerts;

public sealed class DashboardApiTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task Empty_tenant_has_zeroed_sections()
    {
        var body = await GetDashboardAsync();

        var alerts = body.GetProperty("alerts");
        Assert.Equal(0, alerts.GetProperty("open").GetInt32());
        Assert.Equal(14, alerts.GetProperty("trend").GetArrayLength());
        Assert.Equal(0, body.GetProperty("cases").GetProperty("open").GetInt32());
        Assert.Equal(0, body.GetProperty("transactions").GetProperty("processedToday").GetInt32());
        Assert.Equal(1, body.GetProperty("risk").GetProperty("byBand").GetProperty("LOW").GetInt32());
    }

    [Fact]
    public async Task Counts_alerts_rules_cases_transactions_and_risk()
    {
        var caseAlert = await _fx.CreateStructuringAlertAsync();
        (await _fx.AdminClient.PostAsync($"/api/v1/alerts/{caseAlert}/create-case", null)).EnsureSuccessStatusCode();
        var dismissed = await _fx.SeedAlertAsync();
        (await _fx.AdminClient.PostAsJsonAsync($"/api/v1/alerts/{dismissed}/dismiss", new { reason = "False positive" }))
            .EnsureSuccessStatusCode();
        await _fx.SeedAlertAsync();

        var body = await GetDashboardAsync();

        var alerts = body.GetProperty("alerts");
        Assert.Equal(2, alerts.GetProperty("open").GetInt32());
        Assert.Equal(3, alerts.GetProperty("today").GetInt32());
        Assert.Equal(1, alerts.GetProperty("openBySeverity").GetProperty("HIGH").GetInt32());
        Assert.Equal(1, alerts.GetProperty("openBySeverity").GetProperty("MEDIUM").GetInt32());
        Assert.Equal(1, alerts.GetProperty("highRiskOpen").GetInt32());
        Assert.Equal(2, alerts.GetProperty("openByAge").GetProperty("underOneDay").GetInt32());
        Assert.Equal(0, alerts.GetProperty("slaBreaches").GetInt32());
        Assert.Equal(3, alerts.GetProperty("trend")[13].GetProperty("count").GetInt32());

        var rules = alerts.GetProperty("byRule").EnumerateArray().ToList();
        var structuring = rules.Single(r => r.GetProperty("ruleCode").GetString()!.Length > 0 && r.GetProperty("total").GetInt32() == 1 && r.GetProperty("dismissed").GetInt32() == 0);
        Assert.False(string.IsNullOrEmpty(structuring.GetProperty("ruleName").GetString()));
        Assert.Contains(rules, r => r.GetProperty("dismissed").GetInt32() == 1 && r.GetProperty("dismissalRate").GetDouble() == 1.0);

        var cases = body.GetProperty("cases");
        Assert.Equal(1, cases.GetProperty("open").GetInt32());
        Assert.Equal(1, cases.GetProperty("byStatus").GetProperty("OPEN").GetInt32());
        Assert.Equal(0, cases.GetProperty("overdue").GetInt32());

        Assert.Equal(5, body.GetProperty("transactions").GetProperty("processedToday").GetInt32());

        var risk = body.GetProperty("risk");
        Assert.Equal(1, risk.GetProperty("scoredCustomers").GetInt32());
        Assert.Equal(0, risk.GetProperty("unscoredCustomers").GetInt32());
    }

    private async Task<JsonElement> GetDashboardAsync()
    {
        var response = await _fx.AdminClient.GetAsync("/api/v1/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
