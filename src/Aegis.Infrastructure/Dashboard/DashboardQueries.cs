namespace Aegis.Infrastructure.Dashboard;

using Aegis.Application.Dashboard;
using Aegis.Infrastructure.Persistence;
using Aegis.Modules.Alerts.Domain;
using Aegis.Modules.Cases.Domain;
using Aegis.Modules.Risk.Domain;
using Aegis.Shared.Domain;
using Microsoft.EntityFrameworkCore;

public sealed class DashboardQueries : IDashboardQueries
{
    private readonly AegisDbContext _db;
    private readonly DashboardOptions _options;

    public DashboardQueries(AegisDbContext db, DashboardOptions options)
    {
        _db = db;
        _options = options;
    }

    private sealed class DayCountRow
    {
        public DateOnly Day { get; set; }
        public int Count { get; set; }
    }

    private sealed class BandCountRow
    {
        public string Band { get; set; } = null!;
        public int Count { get; set; }
    }

    public async Task<AlertMetrics> GetAlertMetricsAsync(TenantId tenantId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var alerts = _db.Alerts.AsNoTracking().Where(a => a.TenantId == tenantId);
        var open = alerts.Where(a => a.Status != AlertStatus.RESOLVED && a.Status != AlertStatus.DISMISSED && a.Status != AlertStatus.CLOSED);
        var todayStart = StartOfUtcDay(now);
        var oneDay = now.AddDays(-1);
        var threeDays = now.AddDays(-3);
        var sevenDays = now.AddDays(-7);
        var slaCutoff = now.AddDays(-_options.AlertSlaDays);

        var bySeverity = await open
            .GroupBy(a => a.Severity)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        var ages = await open
            .GroupBy(_ => 1)
            .Select(g => new
            {
                UnderOne = g.Count(a => a.TriggeredAt >= oneDay),
                OneToThree = g.Count(a => a.TriggeredAt < oneDay && a.TriggeredAt >= threeDays),
                ThreeToSeven = g.Count(a => a.TriggeredAt < threeDays && a.TriggeredAt >= sevenDays),
                OverSeven = g.Count(a => a.TriggeredAt < sevenDays),
                Breached = g.Count(a => a.TriggeredAt < slaCutoff)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var today = await alerts.CountAsync(a => a.TriggeredAt >= todayStart, cancellationToken);

        var trendStart = todayStart.AddDays(-(_options.TrendDays - 1));
        var trendRows = await _db.Database.SqlQuery<DayCountRow>($"""
            SELECT ("TriggeredAt" AT TIME ZONE 'UTC')::date AS "Day", count(*)::int AS "Count"
            FROM alerts.alerts
            WHERE tenant_id = {tenantId.Value} AND "TriggeredAt" >= {trendStart}
            GROUP BY 1
            """).ToListAsync(cancellationToken);

        var ruleWindowStart = now.AddDays(-_options.RuleWindowDays);
        var ruleCounts = await alerts
            .Where(a => a.TriggeredAt >= ruleWindowStart)
            .GroupBy(a => a.RuleId)
            .Select(g => new { RuleId = g.Key, Total = g.Count(), Dismissed = g.Count(a => a.Status == AlertStatus.DISMISSED) })
            .OrderByDescending(x => x.Total)
            .Take(_options.TopRules)
            .ToListAsync(cancellationToken);
        var ruleIds = ruleCounts.Select(r => r.RuleId).ToList();
        var rules = await _db.AmlRules.AsNoTracking()
            .Where(r => r.TenantId == tenantId && ruleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => new { r.Code, r.Name }, cancellationToken);

        return new AlertMetrics(
            Open: bySeverity.Values.Sum(),
            Today: today,
            HighRiskOpen: bySeverity.GetValueOrDefault(AlertSeverity.HIGH) + bySeverity.GetValueOrDefault(AlertSeverity.CRITICAL),
            SlaBreaches: ages?.Breached ?? 0,
            OpenBySeverity: DashboardMath.AllKeys<AlertSeverity>(bySeverity),
            OpenByAge: new AlertAgeBuckets(ages?.UnderOne ?? 0, ages?.OneToThree ?? 0, ages?.ThreeToSeven ?? 0, ages?.OverSeven ?? 0),
            Trend: DashboardMath.FillTrend(trendRows.ToDictionary(r => r.Day, r => r.Count), now, _options.TrendDays),
            ByRule: ruleCounts.Select(r => rules.TryGetValue(r.RuleId, out var rule)
                    ? new RuleAlertStats(r.RuleId, rule.Code, rule.Name, r.Total, r.Dismissed, DashboardMath.Rate(r.Dismissed, r.Total))
                    : new RuleAlertStats(r.RuleId, "", "Unknown rule", r.Total, r.Dismissed, DashboardMath.Rate(r.Dismissed, r.Total)))
                .ToList());
    }

    public async Task<CaseMetrics> GetCaseMetricsAsync(TenantId tenantId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var cases = _db.Cases.AsNoTracking().Where(c => c.TenantId == tenantId);
        var byStatus = await cases
            .GroupBy(c => c.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var slaCutoff = now.AddDays(-_options.CaseSlaDays);
        var overdue = await cases.CountAsync(c => c.Status != CaseStatus.CLOSED && c.OpenedAt < slaCutoff, cancellationToken);

        return new CaseMetrics(
            Open: byStatus.Where(kv => kv.Key != CaseStatus.CLOSED).Sum(kv => kv.Value),
            Overdue: overdue,
            ByStatus: DashboardMath.AllKeys<CaseStatus>(byStatus));
    }

    public async Task<TransactionMetrics> GetTransactionMetricsAsync(TenantId tenantId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var todayStart = StartOfUtcDay(now);
        var weekStart = todayStart.AddDays(-6);
        var counts = await _db.Transactions.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.CreatedAt >= weekStart)
            .GroupBy(_ => 1)
            .Select(g => new { Today = g.Count(t => t.CreatedAt >= todayStart), Week = g.Count() })
            .FirstOrDefaultAsync(cancellationToken);
        return new TransactionMetrics(counts?.Today ?? 0, counts?.Week ?? 0);
    }

    public async Task<RiskMetrics> GetRiskMetricsAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.Database.SqlQuery<BandCountRow>($"""
            SELECT latest."Band" AS "Band", count(*)::int AS "Count"
            FROM (
                SELECT DISTINCT ON (customer_id) "Band"
                FROM risk.customer_risk_scores
                WHERE tenant_id = {tenantId.Value}
                ORDER BY customer_id, "CalculatedAt" DESC
            ) latest
            GROUP BY latest."Band"
            """).ToListAsync(cancellationToken);
        var byBand = rows
            .Where(r => Enum.TryParse<RiskBand>(r.Band, out _))
            .ToDictionary(r => Enum.Parse<RiskBand>(r.Band), r => r.Count);
        var scored = byBand.Values.Sum();
        var customers = await _db.Customers.AsNoTracking().CountAsync(c => c.TenantId == tenantId, cancellationToken);

        return new RiskMetrics(scored, Math.Max(0, customers - scored), DashboardMath.AllKeys<RiskBand>(byBand));
    }

    private static DateTimeOffset StartOfUtcDay(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }
}
