namespace Aegis.Tests.Integration.Alerts;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

public sealed class AlertDedupeTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task Seven_95k_creates_one_alert_and_same_day_dedupes()
    {
        var baseTs = DateTimeOffset.UtcNow.AddHours(-2);
        Guid? firstAlertId = null;

        for (var i = 0; i < 7; i++)
        {
            var alertIds = await _fx.IngestStructuringCreditAsync(baseTs.AddMinutes(i));

            if (i < 4)
            {
                Assert.Empty(alertIds);
            }
            else
            {
                // 5th+ should trigger (count>=5, sum>=450k, max<100k)
                Assert.Single(alertIds);
                firstAlertId ??= alertIds[0];
                Assert.Equal(firstAlertId, alertIds[0]);
            }
        }

        Assert.NotNull(firstAlertId);

        var client = _fx.AdminClient;
        var alerts = await client.GetAsync("/api/v1/alerts");
        Assert.Equal(HttpStatusCode.OK, alerts.StatusCode);
        var list = await alerts.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        Assert.Single(list.GetProperty("items").EnumerateArray());

        var get = await client.GetAsync($"/api/v1/alerts/{firstAlertId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var alert = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(firstAlertId, alert.GetProperty("id").GetGuid());
        Assert.NotEqual(Guid.Empty, alert.GetProperty("ruleVersionId").GetGuid());
        Assert.True(alert.GetProperty("transactionIds").GetArrayLength() >= 5);

        var audits = await client.GetAsync("/api/v1/audit-events");
        Assert.Equal(HttpStatusCode.OK, audits.StatusCode);
        var auditList = await audits.Content.ReadFromJsonAsync<JsonElement>();
        var types = auditList.EnumerateArray().Select(e => e.GetProperty("eventType").GetString()).ToList();
        Assert.Contains("TRANSACTION_INGESTED", types);
        Assert.Equal(1, types.Count(t => t == "ALERT_CREATED"));
    }
}
