namespace Aegis.Tests.Integration.Cases;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Tests.Integration.Identity;

public sealed class CaseWorkflowTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("AEGIS_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=aegis_test;Username=aegis;Password=aegis_dev_password";

    private AegisApiFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _customerId;
    private Guid _accountId;

    public async Task InitializeAsync()
    {
        _factory = new AegisApiFactory(ConnectionString);
        _client = _factory.CreateClient();

        var slug = $"case-{Guid.NewGuid():N}"[..16];
        (await _client.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "Case Tenant",
            slug,
            adminEmail = $"admin@{slug}.test",
            adminName = "Admin",
            adminPassword = "Passw0rd!"
        })).EnsureSuccessStatusCode();

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = $"admin@{slug}.test",
            password = "Passw0rd!",
            tenantSlug = slug
        });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());

        var customerResp = await _client.PostAsJsonAsync("/api/v1/customers", new
        {
            type = "INDIVIDUAL",
            country = "KE",
            firstName = "Casey",
            lastName = "Case"
        });
        customerResp.EnsureSuccessStatusCode();
        _customerId = (await customerResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var accountResp = await _client.PostAsJsonAsync($"/api/v1/customers/{_customerId}/accounts", new
        {
            accountType = "MOBILE_WALLET",
            currency = "KES"
        });
        accountResp.EnsureSuccessStatusCode();
        _accountId = (await accountResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Alert_to_case_note_close_writes_audit()
    {
        var baseTs = DateTimeOffset.UtcNow.AddHours(-1);
        Guid? alertId = null;
        for (var i = 0; i < 5; i++)
        {
            var ingest = await _client.PostAsJsonAsync("/api/v1/transactions", new
            {
                externalReference = $"TX-CASE-{i}-{Guid.NewGuid():N}"[..28],
                accountId = _accountId,
                customerId = _customerId,
                amount = 95_000m,
                currency = "KES",
                direction = "CREDIT",
                transactionType = "TRANSFER",
                channel = "MOBILE",
                timestamp = baseTs.AddMinutes(i)
            });
            ingest.EnsureSuccessStatusCode();
            var alertIds = (await ingest.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("alertIds").EnumerateArray().Select(x => x.GetGuid()).ToList();
            if (alertIds.Count > 0) alertId = alertIds[0];
        }

        Assert.NotNull(alertId);

        var create = await _client.PostAsync($"/api/v1/alerts/{alertId}/create-case", null);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var caseBody = await create.Content.ReadFromJsonAsync<JsonElement>();
        var caseId = caseBody.GetProperty("id").GetGuid();
        Assert.Equal("OPEN", caseBody.GetProperty("status").GetString());
        Assert.Contains(alertId.Value, caseBody.GetProperty("linkedAlertIds").EnumerateArray().Select(x => x.GetGuid()));

        var note = await _client.PostAsJsonAsync($"/api/v1/cases/{caseId}/notes", new { text = "Reviewed structuring pattern." });
        Assert.Equal(HttpStatusCode.OK, note.StatusCode);

        var close = await _client.PostAsJsonAsync($"/api/v1/cases/{caseId}/close", new
        {
            disposition = "FALSE_POSITIVE",
            conclusion = "Customer activity explained by payroll batching."
        });
        if (close.StatusCode != HttpStatusCode.OK)
        {
            var err = await close.Content.ReadAsStringAsync();
            Assert.Fail($"Close failed: {(int)close.StatusCode} {err}");
        }
        var closed = await close.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CLOSED", closed.GetProperty("status").GetString());
        Assert.Equal("FALSE_POSITIVE", closed.GetProperty("disposition").GetString());

        var audits = await _client.GetAsync("/api/v1/audit-events");
        audits.EnsureSuccessStatusCode();
        var types = (await audits.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray()
            .Select(e => e.GetProperty("eventType").GetString())
            .ToList();
        Assert.Contains("CASE_CREATED", types);
        Assert.Contains("CASE_UPDATED", types);
        Assert.Contains("CASE_CLOSED", types);
    }

    [Fact]
    public async Task Cross_tenant_case_lookup_returns_not_found()
    {
        var baseTs = DateTimeOffset.UtcNow.AddHours(-1);
        Guid? alertId = null;
        for (var i = 0; i < 5; i++)
        {
            var ingest = await _client.PostAsJsonAsync("/api/v1/transactions", new
            {
                externalReference = $"TX-XB-{i}-{Guid.NewGuid():N}"[..28],
                accountId = _accountId,
                customerId = _customerId,
                amount = 95_000m,
                currency = "KES",
                direction = "CREDIT",
                transactionType = "TRANSFER",
                channel = "MOBILE",
                timestamp = baseTs.AddMinutes(i)
            });
            ingest.EnsureSuccessStatusCode();
            var alertIds = (await ingest.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("alertIds").EnumerateArray().Select(x => x.GetGuid()).ToList();
            if (alertIds.Count > 0) alertId = alertIds[0];
        }

        Assert.NotNull(alertId);
        var create = await _client.PostAsync($"/api/v1/alerts/{alertId}/create-case", null);
        create.EnsureSuccessStatusCode();
        var caseId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var otherSlug = $"xb-{Guid.NewGuid():N}"[..16];
        using var other = _factory.CreateClient();
        (await other.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "Other",
            slug = otherSlug,
            adminEmail = $"admin@{otherSlug}.test",
            adminName = "Admin",
            adminPassword = "Passw0rd!"
        })).EnsureSuccessStatusCode();
        var login = await other.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = $"admin@{otherSlug}.test",
            password = "Passw0rd!",
            tenantSlug = otherSlug
        });
        login.EnsureSuccessStatusCode();
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/cases/{caseId}")).StatusCode);
    }
}

