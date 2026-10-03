namespace Aegis.Infrastructure.Persistence.Repositories;

using Aegis.Application.Risk;
using Aegis.Modules.Alerts.Domain;
using Aegis.Modules.Cases.Domain;
using Aegis.Modules.Risk.Application;
using Aegis.Modules.Risk.Domain;
using Aegis.Shared.Domain;
using Microsoft.EntityFrameworkCore;

public sealed class RiskModelRepository : IRiskModelRepository
{
    private readonly AegisDbContext _db;

    public RiskModelRepository(AegisDbContext db) => _db = db;

    public Task<RiskModel?> GetActiveAsync(TenantId tenantId, CancellationToken cancellationToken = default)
        => _db.RiskModels
            .Where(m => m.TenantId == tenantId && m.IsActive)
            .OrderByDescending(m => m.Version)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<RiskModel>> ListVersionsAsync(TenantId tenantId, CancellationToken cancellationToken = default)
        => await _db.RiskModels.AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .OrderByDescending(m => m.Version)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(RiskModel model, CancellationToken cancellationToken = default)
        => await _db.RiskModels.AddAsync(model, cancellationToken);
}

public sealed class CustomerRiskScoreRepository : ICustomerRiskScoreRepository
{
    private readonly AegisDbContext _db;

    public CustomerRiskScoreRepository(AegisDbContext db) => _db = db;

    public async Task<IReadOnlyList<CustomerRiskScore>> ListByCustomerAsync(
        TenantId tenantId, Guid customerId, int take, CancellationToken cancellationToken = default)
        => await _db.CustomerRiskScores.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.CustomerId == customerId)
            .OrderByDescending(s => s.CalculatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(CustomerRiskScore score, CancellationToken cancellationToken = default)
        => await _db.CustomerRiskScores.AddAsync(score, cancellationToken);
}

public sealed class RiskInputsReader : IRiskInputsReader
{
    private const int MaxAlerts = 1000;

    private readonly AegisDbContext _db;

    public RiskInputsReader(AegisDbContext db) => _db = db;

    public async Task<RiskInputs?> ReadAsync(
        TenantId tenantId, Guid customerId, IReadOnlyCollection<int> transactionWindowDays, DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        var customer = await _db.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == customerId)
            .Select(c => new { c.Type, c.Country })
            .FirstOrDefaultAsync(cancellationToken);
        if (customer is null) return null;

        var focusId = customerId.ToString();
        var alerts = await _db.Alerts.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.FocusType == FocusType.CUSTOMER && a.FocusEntityId == focusId)
            .OrderByDescending(a => a.TriggeredAt)
            .Take(MaxAlerts)
            .Select(a => new { a.Severity, a.Status, a.TriggeredAt })
            .ToListAsync(cancellationToken);
        var alertFacts = alerts
            .Select(a => new RiskAlertFact(
                a.Severity,
                a.Status is not (AlertStatus.RESOLVED or AlertStatus.DISMISSED or AlertStatus.CLOSED),
                a.TriggeredAt))
            .ToList();

        var suspiciousCases = await _db.Cases.AsNoTracking()
            .CountAsync(c => c.TenantId == tenantId
                             && c.CustomerId == customerId
                             && (c.Disposition == CaseDisposition.SUSPICIOUS_ACTIVITY || c.Disposition == CaseDisposition.REPORTED),
                cancellationToken);

        var cid = new CustomerId(customerId);
        var transactionCounts = new Dictionary<int, int>();
        foreach (var days in transactionWindowDays.Distinct())
        {
            var since = asOf.AddDays(-days);
            transactionCounts[days] = await _db.Transactions.AsNoTracking()
                .CountAsync(t => t.TenantId == tenantId && t.CustomerId == cid && t.Timestamp >= since && t.Timestamp <= asOf,
                    cancellationToken);
        }

        return new RiskInputs(customer.Type.ToString(), customer.Country, alertFacts, suspiciousCases, transactionCounts, asOf);
    }
}
