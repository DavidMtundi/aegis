namespace Aegis.Tests.EndToEnd.Support;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

public sealed class TenantSession
{
    public required string Slug { get; init; }
    public required Guid TenantId { get; init; }
    public required string AccessToken { get; init; }
    public required HttpClient Client { get; init; }
    public Guid CustomerId { get; set; }
    public Guid AccountId { get; set; }
}

public static class SliceHelpers
{
    public static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("AEGIS_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=aegis_test;Username=aegis;Password=aegis_dev_password";

    public static async Task EnsureDatabaseAsync()
    {
        var adminCs = "Host=localhost;Port=5432;Database=aegis;Username=aegis;Password=aegis_dev_password";
        await using var conn = new Npgsql.NpgsqlConnection(adminCs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM pg_database WHERE datname = 'aegis_test'";
        var exists = await cmd.ExecuteScalarAsync();
        if (exists is null)
        {
            cmd.CommandText = "CREATE DATABASE aegis_test";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public static async Task<TenantSession> BootstrapTenantAsync(AegisApiFactory factory, string namePrefix)
    {
        var client = factory.CreateClient();
        var slug = $"{namePrefix}-{Guid.NewGuid():N}"[..16];
        var bootstrap = await client.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = $"{namePrefix} Tenant",
            slug,
            adminEmail = $"admin@{slug}.test",
            adminName = "Admin",
            adminPassword = "Passw0rd!"
        });
        bootstrap.EnsureSuccessStatusCode();
        var tenantBody = await bootstrap.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = tenantBody.GetProperty("tenantId").GetGuid();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = $"admin@{slug}.test",
            password = "Passw0rd!",
            tenantSlug = slug
        });
        login.EnsureSuccessStatusCode();
        var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();
        var token = loginBody.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return new TenantSession
        {
            Slug = slug,
            TenantId = tenantId,
            AccessToken = token,
            Client = client
        };
    }

    public static async Task CreateCustomerAndAccountAsync(TenantSession session)
    {
        var customerResp = await session.Client.PostAsJsonAsync("/api/v1/customers", new
        {
            type = "INDIVIDUAL",
            country = "KE",
            firstName = "E2E",
            lastName = "Customer"
        });
        customerResp.EnsureSuccessStatusCode();
        session.CustomerId = (await customerResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var accountResp = await session.Client.PostAsJsonAsync($"/api/v1/customers/{session.CustomerId}/accounts", new
        {
            accountType = "MOBILE_WALLET",
            currency = "KES"
        });
        accountResp.EnsureSuccessStatusCode();
        session.AccountId = (await accountResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public static async Task<JsonElement> IngestAsync(
        TenantSession session,
        string externalReference,
        decimal amount,
        DateTimeOffset timestamp)
    {
        var resp = await session.Client.PostAsJsonAsync("/api/v1/transactions", new
        {
            externalReference,
            accountId = session.AccountId,
            customerId = session.CustomerId,
            amount,
            currency = "KES",
            direction = "CREDIT",
            transactionType = "TRANSFER",
            channel = "MOBILE",
            timestamp
        });
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<HttpResponseMessage> IngestRawAsync(
        TenantSession session,
        string externalReference,
        decimal amount,
        DateTimeOffset timestamp)
        => await session.Client.PostAsJsonAsync("/api/v1/transactions", new
        {
            externalReference,
            accountId = session.AccountId,
            customerId = session.CustomerId,
            amount,
            currency = "KES",
            direction = "CREDIT",
            transactionType = "TRANSFER",
            channel = "MOBILE",
            timestamp
        });

    public static async Task<IReadOnlyList<JsonElement>> ListAlertsAsync(TenantSession session)
    {
        var resp = await session.Client.GetAsync("/api/v1/alerts");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("items").EnumerateArray().ToList();
    }

    public static async Task<IReadOnlyList<JsonElement>> ListAuditsAsync(TenantSession session)
    {
        var resp = await session.Client.GetAsync("/api/v1/audit-events");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.EnumerateArray().ToList();
    }
}
