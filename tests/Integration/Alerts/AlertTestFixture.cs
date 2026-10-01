namespace Aegis.Tests.Integration.Alerts;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Infrastructure.Auth;
using Aegis.Modules.Identity.Application;
using Aegis.Modules.Identity.Domain;
using Aegis.Shared.Domain;
using Aegis.Shared.Persistence;
using Aegis.Tests.Integration.Identity;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Fresh tenant with an admin client, one customer, and one KES wallet account.
/// </summary>
public sealed class AlertTestFixture
{
    private const string Password = "Passw0rd!";

    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("AEGIS_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=aegis_test;Username=aegis;Password=aegis_dev_password";

    private readonly List<HttpClient> _clients = new();

    public AegisApiFactory Factory { get; private set; } = null!;
    public HttpClient AdminClient { get; private set; } = null!;
    public Guid TenantId { get; private set; }
    public string Slug { get; private set; } = null!;
    public Guid CustomerId { get; private set; }
    public Guid AccountId { get; private set; }

    public async Task InitializeAsync()
    {
        Factory = new AegisApiFactory(ConnectionString);
        AdminClient = Factory.CreateClient();
        _clients.Add(AdminClient);

        Slug = $"alert-{Guid.NewGuid():N}"[..16];
        var bootstrap = await AdminClient.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "Alert Tenant",
            slug = Slug,
            adminEmail = $"admin@{Slug}.test",
            adminName = "Admin",
            adminPassword = Password
        });
        bootstrap.EnsureSuccessStatusCode();
        TenantId = (await bootstrap.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();

        AdminClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await LoginAsync(AdminClient, $"admin@{Slug}.test"));

        var customerResp = await AdminClient.PostAsJsonAsync("/api/v1/customers", new
        {
            type = "INDIVIDUAL",
            country = "KE",
            firstName = "Sam",
            lastName = "Struct"
        });
        customerResp.EnsureSuccessStatusCode();
        CustomerId = (await customerResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var accountResp = await AdminClient.PostAsJsonAsync($"/api/v1/customers/{CustomerId}/accounts", new
        {
            accountType = "MOBILE_WALLET",
            currency = "KES"
        });
        accountResp.EnsureSuccessStatusCode();
        AccountId = (await accountResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public Task DisposeAsync()
    {
        foreach (var client in _clients) client.Dispose();
        Factory.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Ingests one KES 95,000 credit and returns the alert ids it produced.</summary>
    public async Task<IReadOnlyList<Guid>> IngestStructuringCreditAsync(DateTimeOffset timestamp)
    {
        var ingest = await AdminClient.PostAsJsonAsync("/api/v1/transactions", new
        {
            externalReference = $"TX-ALERT-{Guid.NewGuid():N}"[..28],
            accountId = AccountId,
            customerId = CustomerId,
            amount = 95_000m,
            currency = "KES",
            direction = "CREDIT",
            transactionType = "TRANSFER",
            channel = "MOBILE",
            timestamp
        });
        if (ingest.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Ingest failed: {ingest.StatusCode}");
        var body = await ingest.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("alertIds").EnumerateArray().Select(x => x.GetGuid()).ToList();
    }

    /// <summary>Ingests five structuring-sized credits and returns the resulting alert id.</summary>
    public async Task<Guid> CreateStructuringAlertAsync()
    {
        var baseTs = DateTimeOffset.UtcNow.AddHours(-2);
        IReadOnlyList<Guid> alertIds = Array.Empty<Guid>();
        for (var i = 0; i < 5; i++)
        {
            alertIds = await IngestStructuringCreditAsync(baseTs.AddMinutes(i));
        }
        return alertIds.Single();
    }

    public async Task<HttpClient> CreateUserClientAsync(string email, string[] roles)
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await users.AddAsync(User.Create(new TenantId(TenantId), email, email, hasher.Hash(Password), roles));
            await uow.SaveChangesAsync();
        }

        var client = Factory.CreateClient();
        _clients.Add(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await LoginAsync(client, email));
        return client;
    }

    private async Task<string> LoginAsync(HttpClient client, string email)
    {
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = Password,
            tenantSlug = Slug
        });
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }
}
