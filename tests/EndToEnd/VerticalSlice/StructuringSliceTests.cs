namespace Aegis.Tests.EndToEnd.VerticalSlice;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Tests.EndToEnd.Support;

/// <summary>
/// End-to-end proof of the approved vertical slice:
/// ingest → features → STRUCTURING_001 → alert → audit, with isolation and idempotency.
/// </summary>
public sealed class StructuringSliceTests : IAsyncLifetime
{
    private AegisApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        await SliceHelpers.EnsureDatabaseAsync();
        _factory = new AegisApiFactory(SliceHelpers.ConnectionString);
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Positive_seven_x_95k_produces_one_alert_with_evidence_and_audits()
    {
        var tenant = await SliceHelpers.BootstrapTenantAsync(_factory, "pos");
        await SliceHelpers.CreateCustomerAndAccountAsync(tenant);

        var baseTs = DateTimeOffset.UtcNow.AddHours(-3);
        JsonElement? lastIngest = null;
        for (var i = 0; i < 7; i++)
        {
            lastIngest = await SliceHelpers.IngestAsync(
                tenant,
                $"E2E-POS-{i}-{Guid.NewGuid():N}"[..32],
                95_000m,
                baseTs.AddMinutes(i));
        }

        Assert.NotNull(lastIngest);
        var evaluations = lastIngest.Value.GetProperty("evaluations").EnumerateArray().ToList();
        var structuringEval = evaluations.First(e => e.GetProperty("ruleCode").GetString() == "STRUCTURING_001");
        var features = structuringEval.GetProperty("features");
        Assert.Equal(7, features.GetProperty("transaction_count_24h").GetInt32());
        Assert.Equal(665_000m, features.GetProperty("transaction_sum_24h").GetDecimal());
        Assert.Equal(95_000m, features.GetProperty("max_single_amount_24h").GetDecimal());
        Assert.True(structuringEval.GetProperty("isTriggered").GetBoolean());

        var alerts = await SliceHelpers.ListAlertsAsync(tenant);
        Assert.Single(alerts);
        var alert = alerts[0];
        Assert.NotEqual(Guid.Empty, alert.GetProperty("ruleVersionId").GetGuid());
        // Evidence is captured when the alert is first created (at the 5th tx); later same-day
        // triggers dedupe without rewriting evidence.
        Assert.True(alert.GetProperty("transactionIds").GetArrayLength() >= 5);
        Assert.Contains(
            alert.GetProperty("evaluatedValues").EnumerateObject(),
            p => p.NameEquals("transaction_count_24h"));

        var audits = await SliceHelpers.ListAuditsAsync(tenant);
        var types = audits.Select(a => a.GetProperty("eventType").GetString()).ToList();
        Assert.Equal(7, types.Count(t => t == "TRANSACTION_INGESTED"));
        Assert.True(types.Count(t => t == "ALERT_CREATED") >= 1);
    }

    [Fact]
    public async Task Negative_four_x_95k_produces_zero_alerts()
    {
        var tenant = await SliceHelpers.BootstrapTenantAsync(_factory, "neg");
        await SliceHelpers.CreateCustomerAndAccountAsync(tenant);

        var baseTs = DateTimeOffset.UtcNow.AddHours(-3);
        for (var i = 0; i < 4; i++)
        {
            var ingest = await SliceHelpers.IngestAsync(
                tenant,
                $"E2E-NEG-{i}-{Guid.NewGuid():N}"[..32],
                95_000m,
                baseTs.AddMinutes(i));
            Assert.Empty(ingest.GetProperty("alertIds").EnumerateArray());
        }

        var alerts = await SliceHelpers.ListAlertsAsync(tenant);
        Assert.Empty(alerts);
    }

    [Fact]
    public async Task Boundary_five_x_90k_triggers()
    {
        var tenant = await SliceHelpers.BootstrapTenantAsync(_factory, "bnd");
        await SliceHelpers.CreateCustomerAndAccountAsync(tenant);

        var baseTs = DateTimeOffset.UtcNow.AddHours(-3);
        JsonElement? last = null;
        for (var i = 0; i < 5; i++)
        {
            last = await SliceHelpers.IngestAsync(
                tenant,
                $"E2E-BND-{i}-{Guid.NewGuid():N}"[..32],
                90_000m,
                baseTs.AddMinutes(i));
        }

        Assert.NotNull(last);
        var structuringEval = last.Value.GetProperty("evaluations").EnumerateArray()
            .First(e => e.GetProperty("ruleCode").GetString() == "STRUCTURING_001");
        Assert.True(structuringEval.GetProperty("isTriggered").GetBoolean());
        Assert.Single(last.Value.GetProperty("alertIds").EnumerateArray());
        Assert.Single(await SliceHelpers.ListAlertsAsync(tenant));
    }

    [Fact]
    public async Task Duplicate_external_reference_is_idempotent()
    {
        var tenant = await SliceHelpers.BootstrapTenantAsync(_factory, "dup");
        await SliceHelpers.CreateCustomerAndAccountAsync(tenant);

        var baseTs = DateTimeOffset.UtcNow.AddHours(-3);
        for (var i = 0; i < 5; i++)
        {
            await SliceHelpers.IngestAsync(
                tenant,
                $"E2E-DUP-SETUP-{i}-{Guid.NewGuid():N}"[..32],
                95_000m,
                baseTs.AddMinutes(i));
        }

        var alertsBefore = await SliceHelpers.ListAlertsAsync(tenant);
        Assert.Single(alertsBefore);
        var auditsBefore = (await SliceHelpers.ListAuditsAsync(tenant))
            .Count(a => a.GetProperty("eventType").GetString() == "TRANSACTION_INGESTED");

        var externalRef = $"E2E-DUP-SAME-{Guid.NewGuid():N}"[..28];
        var first = await SliceHelpers.IngestAsync(tenant, externalRef, 95_000m, baseTs.AddMinutes(10));
        Assert.True(first.GetProperty("wasCreated").GetBoolean());
        var txId = first.GetProperty("transactionId").GetGuid();

        var secondResp = await SliceHelpers.IngestRawAsync(tenant, externalRef, 95_000m, baseTs.AddMinutes(10));
        Assert.Equal(HttpStatusCode.OK, secondResp.StatusCode);
        var second = await secondResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(second.GetProperty("wasCreated").GetBoolean());
        Assert.Equal(txId, second.GetProperty("transactionId").GetGuid());

        Assert.Single(await SliceHelpers.ListAlertsAsync(tenant));
        var auditsAfter = (await SliceHelpers.ListAuditsAsync(tenant))
            .Count(a => a.GetProperty("eventType").GetString() == "TRANSACTION_INGESTED");
        Assert.Equal(auditsBefore + 1, auditsAfter);
    }

    [Fact]
    public async Task Cross_tenant_cannot_read_tenant_a_resources()
    {
        var tenantA = await SliceHelpers.BootstrapTenantAsync(_factory, "xa");
        await SliceHelpers.CreateCustomerAndAccountAsync(tenantA);
        var baseTs = DateTimeOffset.UtcNow.AddHours(-3);
        JsonElement? lastIngest = null;
        for (var i = 0; i < 5; i++)
        {
            lastIngest = await SliceHelpers.IngestAsync(
                tenantA,
                $"E2E-XA-{i}-{Guid.NewGuid():N}"[..32],
                95_000m,
                baseTs.AddMinutes(i));
        }

        Assert.NotNull(lastIngest);
        var alertsA = await SliceHelpers.ListAlertsAsync(tenantA);
        Assert.Single(alertsA);
        var alertId = alertsA[0].GetProperty("id").GetGuid();
        var txId = lastIngest.Value.GetProperty("transactionId").GetGuid();
        var customerId = tenantA.CustomerId;

        var tenantB = await SliceHelpers.BootstrapTenantAsync(_factory, "xb");

        Assert.Equal(HttpStatusCode.NotFound, (await tenantB.Client.GetAsync($"/api/v1/alerts/{alertId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tenantB.Client.GetAsync($"/api/v1/customers/{customerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tenantB.Client.GetAsync($"/api/v1/transactions/{txId}")).StatusCode);
    }

    [Fact]
    public async Task Future_timestamp_rejected_and_amount_decimal_round_trips()
    {
        var tenant = await SliceHelpers.BootstrapTenantAsync(_factory, "val");
        await SliceHelpers.CreateCustomerAndAccountAsync(tenant);

        var future = await SliceHelpers.IngestRawAsync(
            tenant,
            $"E2E-FUT-{Guid.NewGuid():N}"[..28],
            1000m,
            DateTimeOffset.UtcNow.AddHours(2));
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);

        var amount = 95000.00m;
        var ingest = await SliceHelpers.IngestAsync(
            tenant,
            $"E2E-DEC-{Guid.NewGuid():N}"[..28],
            amount,
            DateTimeOffset.UtcNow.AddMinutes(-5));
        var txId = ingest.GetProperty("transactionId").GetGuid();
        var get = await tenant.Client.GetAsync($"/api/v1/transactions/{txId}");
        get.EnsureSuccessStatusCode();
        var body = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(amount, body.GetProperty("amount").GetDecimal());
        Assert.Equal("KES", body.GetProperty("currency").GetString());
    }
}
