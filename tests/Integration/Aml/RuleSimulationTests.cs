namespace Aegis.Tests.Integration.Aml;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Tests.Integration.Alerts;

public sealed class RuleSimulationTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task Simulate_active_structuring_rule_returns_counts_without_writing_alerts()
    {
        // Seed a little history so sampling is non-empty.
        for (var i = 0; i < 5; i++)
            await _fx.IngestStructuringCreditAsync(DateTimeOffset.UtcNow.AddHours(-2).AddMinutes(i));

        var rules = await _fx.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/rules");
        var structuring = rules.EnumerateArray()
            .First(r => r.GetProperty("code").GetString() == "STRUCTURING_001");
        var versionId = structuring.GetProperty("ruleVersionId").GetGuid();

        var beforeAlerts = await _fx.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/alerts?pageSize=1");
        var beforeTotal = beforeAlerts.GetProperty("totalCount").GetInt32();

        var simulate = await _fx.AdminClient.PostAsJsonAsync(
            $"/api/v1/rules/versions/{versionId}/simulate",
            new { lookbackDays = 30 });
        Assert.Equal(HttpStatusCode.OK, simulate.StatusCode);
        var body = await simulate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(versionId, body.GetProperty("versionId").GetGuid());
        Assert.True(body.GetProperty("customersSampled").GetInt32() >= 1);
        Assert.True(body.GetProperty("transactionsInSample").GetInt32() >= 1);
        Assert.True(body.GetProperty("candidateTriggers").GetInt32() >= 0);

        var afterAlerts = await _fx.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/alerts?pageSize=1");
        Assert.Equal(beforeTotal, afterAlerts.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Simulate_rejects_invalid_lookback()
    {
        var rules = await _fx.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/rules");
        var versionId = rules.EnumerateArray().First().GetProperty("ruleVersionId").GetGuid();
        var bad = await _fx.AdminClient.PostAsJsonAsync(
            $"/api/v1/rules/versions/{versionId}/simulate",
            new { lookbackDays = 999 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }
}
