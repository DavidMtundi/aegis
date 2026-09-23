namespace Aegis.Tests.Integration.Concurrency;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Tests.Integration.Identity;

public sealed class ConcurrentIdempotencyTests : IAsyncLifetime
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

        var slug = $"race-{Guid.NewGuid():N}"[..16];
        (await _client.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "Race Tenant",
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

        var customer = await _client.PostAsJsonAsync("/api/v1/customers", new
        {
            type = "INDIVIDUAL",
            country = "KE",
            firstName = "Race",
            lastName = "Test"
        });
        customer.EnsureSuccessStatusCode();
        _customerId = (await customer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var account = await _client.PostAsJsonAsync($"/api/v1/customers/{_customerId}/accounts", new
        {
            accountType = "MOBILE_WALLET",
            currency = "KES"
        });
        account.EnsureSuccessStatusCode();
        _accountId = (await account.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Concurrent_same_external_reference_is_idempotent_without_500()
    {
        var ext = $"RACE-{Guid.NewGuid():N}";
        var ts = DateTimeOffset.UtcNow.AddMinutes(-30);
        var payload = new
        {
            externalReference = ext,
            accountId = _accountId,
            customerId = _customerId,
            amount = 1000m,
            currency = "KES",
            direction = "CREDIT",
            transactionType = "TRANSFER",
            channel = "MOBILE",
            timestamp = ts
        };

        var statuses = new ConcurrentBag<HttpStatusCode>();
        var bodies = new ConcurrentBag<JsonElement>();

        await Parallel.ForEachAsync(Enumerable.Range(0, 20), async (_, ct) =>
        {
            // Each request needs its own HttpClient or careful concurrency — share factory.
            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = _client.DefaultRequestHeaders.Authorization;
            var resp = await client.PostAsJsonAsync("/api/v1/transactions", payload, ct);
            statuses.Add(resp.StatusCode);
            if (resp.IsSuccessStatusCode)
            {
                bodies.Add(await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct));
            }
        });

        Assert.DoesNotContain(HttpStatusCode.InternalServerError, statuses);
        Assert.All(statuses, s => Assert.True(s is HttpStatusCode.OK or HttpStatusCode.Created));

        var created = bodies.Count(b => b.GetProperty("wasCreated").GetBoolean());
        var dupes = bodies.Count(b => !b.GetProperty("wasCreated").GetBoolean());
        var ids = bodies.Select(b => b.GetProperty("transactionId").GetGuid()).Distinct().ToList();

        Assert.Equal(1, created);
        Assert.Equal(19, dupes);
        Assert.Single(ids);
    }
}
