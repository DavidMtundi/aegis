namespace Aegis.Tests.Integration.Customers;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Tests.Integration.Identity;

public sealed class CustomerTransactionTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("AEGIS_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=aegis_test;Username=aegis;Password=aegis_dev_password";

    private AegisApiFactory _factory = null!;
    private HttpClient _client = null!;
    private string _token = null!;

    public async Task InitializeAsync()
    {
        _factory = new AegisApiFactory(ConnectionString);
        _client = _factory.CreateClient();

        var slug = $"cust-{Guid.NewGuid():N}"[..16];
        var bootstrap = await _client.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "Cust Tenant",
            slug,
            adminEmail = $"admin@{slug}.test",
            adminName = "Admin",
            adminPassword = "Passw0rd!"
        });
        bootstrap.EnsureSuccessStatusCode();

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = $"admin@{slug}.test",
            password = "Passw0rd!",
            tenantSlug = slug
        });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        _token = body.GetProperty("accessToken").GetString()!;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Create_customer_account_and_idempotent_transaction_ingest()
    {
        var customerResp = await _client.PostAsJsonAsync("/api/v1/customers", new
        {
            type = "INDIVIDUAL",
            country = "KE",
            firstName = "Ada",
            lastName = "Okello"
        });
        Assert.Equal(HttpStatusCode.Created, customerResp.StatusCode);
        var customer = await customerResp.Content.ReadFromJsonAsync<JsonElement>();
        var customerId = customer.GetProperty("id").GetGuid();

        var accountResp = await _client.PostAsJsonAsync($"/api/v1/customers/{customerId}/accounts", new
        {
            accountType = "MOBILE_WALLET",
            currency = "KES"
        });
        Assert.Equal(HttpStatusCode.Created, accountResp.StatusCode);
        var account = await accountResp.Content.ReadFromJsonAsync<JsonElement>();
        var accountId = account.GetProperty("id").GetGuid();

        var ts = DateTimeOffset.UtcNow.AddMinutes(-10);
        var ingest1 = await _client.PostAsJsonAsync("/api/v1/transactions", new
        {
            externalReference = "TX-IDEMP-1",
            accountId,
            customerId,
            amount = 95000m,
            currency = "KES",
            direction = "CREDIT",
            transactionType = "TRANSFER",
            channel = "MOBILE",
            timestamp = ts
        });
        Assert.Equal(HttpStatusCode.Created, ingest1.StatusCode);
        var tx1 = await ingest1.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(tx1.GetProperty("wasCreated").GetBoolean());
        var txId = tx1.GetProperty("transactionId").GetGuid();

        var ingest2 = await _client.PostAsJsonAsync("/api/v1/transactions", new
        {
            externalReference = "TX-IDEMP-1",
            accountId,
            customerId,
            amount = 95000m,
            currency = "KES",
            direction = "CREDIT",
            transactionType = "TRANSFER",
            channel = "MOBILE",
            timestamp = ts
        });
        Assert.Equal(HttpStatusCode.OK, ingest2.StatusCode);
        var tx2 = await ingest2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(tx2.GetProperty("wasCreated").GetBoolean());
        Assert.Equal(txId, tx2.GetProperty("transactionId").GetGuid());

        var get = await _client.GetAsync($"/api/v1/transactions/{txId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var got = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(95000m, got.GetProperty("amount").GetDecimal());
        Assert.Equal("KES", got.GetProperty("currency").GetString());

        var future = await _client.PostAsJsonAsync("/api/v1/transactions", new
        {
            externalReference = "TX-FUTURE",
            accountId,
            customerId,
            amount = 1000m,
            currency = "KES",
            direction = "CREDIT",
            transactionType = "TRANSFER",
            channel = "MOBILE",
            timestamp = DateTimeOffset.UtcNow.AddHours(2)
        });
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
    }
}
