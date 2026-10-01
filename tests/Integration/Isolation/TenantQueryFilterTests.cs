namespace Aegis.Tests.Integration.Isolation;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Infrastructure.Persistence;
using Aegis.Shared.Domain;
using Aegis.Shared.Security;
using Aegis.Tests.Integration.Alerts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Two tenants each with a customer, account, transactions, alert, and case.
/// Queries without an explicit tenant predicate must only see the signed-in tenant's rows.
/// </summary>
public sealed class TenantQueryFilterTests : IAsyncLifetime
{
    private readonly AlertTestFixture _a = new();
    private readonly AlertTestFixture _b = new();
    private Guid _alertA;
    private Guid _caseA;

    public async Task InitializeAsync()
    {
        await _a.InitializeAsync();
        await _b.InitializeAsync();

        _alertA = await _a.CreateStructuringAlertAsync();
        var createdA = await _a.AdminClient.PostAsync($"/api/v1/alerts/{_alertA}/create-case", null);
        createdA.EnsureSuccessStatusCode();
        _caseA = (await createdA.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var alertB = await _b.CreateStructuringAlertAsync();
        (await _b.AdminClient.PostAsync($"/api/v1/alerts/{alertB}/create-case", null)).EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        await _b.DisposeAsync();
        await _a.DisposeAsync();
    }

    [Fact]
    public async Task Unfiltered_queries_only_return_the_signed_in_tenants_rows()
    {
        await using var scope = _a.Factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenant.TenantId = new TenantId(_a.TenantId);
        tenant.UserId = Guid.NewGuid();
        tenant.IsAuthenticated = true;
        var db = scope.ServiceProvider.GetRequiredService<AegisDbContext>();
        var expected = new TenantId(_a.TenantId);

        Assert.All(await db.Customers.ToListAsync(), x => Assert.Equal(expected, x.TenantId));
        Assert.All(await db.Accounts.ToListAsync(), x => Assert.Equal(expected, x.TenantId));
        Assert.All(await db.Transactions.ToListAsync(), x => Assert.Equal(expected, x.TenantId));
        Assert.All(await db.Alerts.ToListAsync(), x => Assert.Equal(expected, x.TenantId));
        Assert.All(await db.Cases.ToListAsync(), x => Assert.Equal(expected, x.TenantId));
        Assert.All(await db.AmlRules.ToListAsync(), x => Assert.Equal(expected, x.TenantId));
        Assert.All(await db.AmlRuleVersions.ToListAsync(), x => Assert.Equal(expected, x.TenantId));
        Assert.All(await db.Users.ToListAsync(), x => Assert.Equal(expected, x.TenantId));
        Assert.All(await db.AuditEvents.ToListAsync(), x => Assert.Equal(_a.TenantId, x.TenantId));

        Assert.NotEmpty(await db.Alerts.ToListAsync());
    }

    [Fact]
    public async Task Other_tenant_cannot_read_alert_case_or_audit_through_the_api()
    {
        var alert = await _b.AdminClient.GetAsync($"/api/v1/alerts/{_alertA}");
        var complianceCase = await _b.AdminClient.GetAsync($"/api/v1/cases/{_caseA}");
        var audit = await _b.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/audit-events");

        Assert.Equal(HttpStatusCode.NotFound, alert.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, complianceCase.StatusCode);
        Assert.All(audit.EnumerateArray(), e => Assert.Equal(_b.TenantId, e.GetProperty("tenantId").GetGuid()));
        Assert.DoesNotContain(audit.EnumerateArray(), e => e.GetProperty("entityId").GetString() == _alertA.ToString());
    }
}
