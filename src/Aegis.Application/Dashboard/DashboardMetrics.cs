namespace Aegis.Application.Dashboard;

using Aegis.Shared.Domain;

/// <summary>SLA thresholds are configurable because the PRD leaves them per institution (Dashboard:* settings).</summary>
public sealed record DashboardOptions(int AlertSlaDays = 3, int CaseSlaDays = 14, int TrendDays = 14, int RuleWindowDays = 30, int TopRules = 10);

public sealed record AlertAgeBuckets(int UnderOneDay, int OneToThreeDays, int ThreeToSevenDays, int OverSevenDays);

public sealed record DailyCount(DateOnly Date, int Count);

public sealed record RuleAlertStats(Guid RuleId, string RuleCode, string RuleName, int Total, int Dismissed, double DismissalRate);

public sealed record AlertMetrics(
    int Open,
    int Today,
    int HighRiskOpen,
    int SlaBreaches,
    IReadOnlyDictionary<string, int> OpenBySeverity,
    AlertAgeBuckets OpenByAge,
    IReadOnlyList<DailyCount> Trend,
    IReadOnlyList<RuleAlertStats> ByRule);

public sealed record CaseMetrics(int Open, int Overdue, IReadOnlyDictionary<string, int> ByStatus);

public sealed record TransactionMetrics(int ProcessedToday, int ProcessedLast7Days);

public sealed record RiskMetrics(int ScoredCustomers, int UnscoredCustomers, IReadOnlyDictionary<string, int> ByBand);

/// <summary>Read-only aggregates for the operations dashboard (PRD §49). "Today" is the current UTC day.</summary>
public interface IDashboardQueries
{
    Task<AlertMetrics> GetAlertMetricsAsync(TenantId tenantId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<CaseMetrics> GetCaseMetricsAsync(TenantId tenantId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<TransactionMetrics> GetTransactionMetricsAsync(TenantId tenantId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<RiskMetrics> GetRiskMetricsAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

public static class DashboardMath
{
    /// <summary>One entry per UTC day ending today, oldest first, with zero for days without data.</summary>
    public static IReadOnlyList<DailyCount> FillTrend(IReadOnlyDictionary<DateOnly, int> counts, DateTimeOffset now, int days)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        return Enumerable.Range(0, days)
            .Select(i => today.AddDays(i - days + 1))
            .Select(d => new DailyCount(d, counts.TryGetValue(d, out var c) ? c : 0))
            .ToList();
    }

    public static double Rate(int part, int total) => total == 0 ? 0 : Math.Round((double)part / total, 4);

    /// <summary>Every enum name as a key so clients never see a missing bucket.</summary>
    public static IReadOnlyDictionary<string, int> AllKeys<TEnum>(IReadOnlyDictionary<TEnum, int> counts) where TEnum : struct, Enum
        => Enum.GetValues<TEnum>().ToDictionary(v => v.ToString(), v => counts.TryGetValue(v, out var c) ? c : 0);
}
