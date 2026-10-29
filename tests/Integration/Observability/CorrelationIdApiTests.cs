namespace Aegis.Tests.Integration.Observability;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Api.Middleware;
using Aegis.Tests.Integration.Identity;

public sealed class CorrelationIdApiTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("AEGIS_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=aegis_test;Username=aegis;Password=aegis_dev_password";

    private AegisApiFactory _factory = null!;
    private HttpClient _client = null!;
    private string _slug = null!;

    public async Task InitializeAsync()
    {
        _factory = new AegisApiFactory(ConnectionString);
        _client = _factory.CreateClient();
        _slug = $"otel-{Guid.NewGuid():N}"[..16];

        (await _client.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "OTel Tenant",
            slug = _slug,
            adminEmail = $"admin@{_slug}.test",
            adminName = "Admin",
            adminPassword = "Passw0rd!"
        })).EnsureSuccessStatusCode();

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = $"admin@{_slug}.test",
            password = "Passw0rd!",
            tenantSlug = _slug
        });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Echoes_correlation_id_and_stores_it_on_audit()
    {
        const string corr = "integration-corr-abc123";
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customers");
        req.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, corr);
        req.Content = JsonContent.Create(new
        {
            type = "INDIVIDUAL",
            country = "KE",
            firstName = "Corr",
            lastName = "Test"
        });

        var response = await _client.SendAsync(req);
        response.EnsureSuccessStatusCode();
        Assert.Equal(corr, response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());

        var audit = await _client.GetAsync("/api/v1/audit-events?pageSize=20");
        audit.EnsureSuccessStatusCode();
        var body = await audit.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.TryGetProperty("items", out var list) ? list : body;
        Assert.Contains(
            items.EnumerateArray(),
            e => e.TryGetProperty("correlationId", out var c) && c.GetString() == corr);
    }
}
