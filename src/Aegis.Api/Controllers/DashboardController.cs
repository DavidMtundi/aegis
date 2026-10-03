namespace Aegis.Api.Controllers;

using Aegis.Application.Dashboard;
using Aegis.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("api/v1/dashboard")]
public sealed class DashboardController : ControllerBase
{
    private readonly IDashboardQueries _queries;
    private readonly DashboardOptions _options;
    private readonly ITenantContext _tenant;

    public DashboardController(IDashboardQueries queries, DashboardOptions options, ITenantContext tenant)
    {
        _queries = queries;
        _options = options;
        _tenant = tenant;
    }

    /// <summary>Sections the caller has no read permission for are null.</summary>
    public sealed record DashboardResponse(
        DateTimeOffset GeneratedAt,
        int AlertSlaDays,
        int CaseSlaDays,
        AlertMetrics? Alerts,
        CaseMetrics? Cases,
        TransactionMetrics? Transactions,
        RiskMetrics? Risk);

    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> Get(CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var now = DateTimeOffset.UtcNow;
        var tenantId = _tenant.TenantId;
        bool Can(string permission) => RolePermissions.Grants(_tenant.Roles, permission);

        // Sequential: the queries share one DbContext, which does not allow concurrent operations.
        var alerts = Can(Permissions.AlertRead) ? await _queries.GetAlertMetricsAsync(tenantId, now, ct) : null;
        var cases = Can(Permissions.CaseRead) ? await _queries.GetCaseMetricsAsync(tenantId, now, ct) : null;
        var transactions = Can(Permissions.TransactionRead) ? await _queries.GetTransactionMetricsAsync(tenantId, now, ct) : null;
        var risk = Can(Permissions.CustomerRead) ? await _queries.GetRiskMetricsAsync(tenantId, ct) : null;

        return new DashboardResponse(now, _options.AlertSlaDays, _options.CaseSlaDays, alerts, cases, transactions, risk);
    }
}
