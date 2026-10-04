namespace Aegis.Modules.Features.Application;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aegis.Modules.Transactions.Application;
using Aegis.Modules.Transactions.Domain;
using Aegis.Shared.Domain;

/// <summary>
/// Customer features as of a transaction. Aggregates only include transactions in the triggering
/// transaction's currency, so thresholds never add amounts across currencies.
/// Fixed-name features (<c>_24h</c>, <c>_1h</c>) keep their windows; generic names
/// (<c>transaction_count</c>, <c>transaction_sum</c>, ...) cover the requested window.
/// </summary>
public sealed class FeatureCalculator : IFeatureCalculator
{
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    private readonly ITransactionReadPort _transactions;

    public FeatureCalculator(ITransactionReadPort transactions) => _transactions = transactions;

    public async Task<FeatureCalculationResult> CalculateAsync(
        TenantId tenantId,
        FocusType focusType,
        string focusEntityId,
        DateTimeOffset asOfTimestamp,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        if (focusType != FocusType.CUSTOMER)
        {
            throw new NotSupportedException("This slice only supports CUSTOMER focus feature calculation.");
        }

        if (!Guid.TryParse(focusEntityId, out var customerGuid))
        {
            throw new ArgumentException("focusEntityId must be a customer GUID.", nameof(focusEntityId));
        }

        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "Window must be positive.");
        }

        var loadWindow = window > Day ? window : Day;
        var loadStart = asOfTimestamp - loadWindow;
        var loaded = await _transactions.GetForCustomerWindowAsync(
            tenantId,
            new CustomerId(customerGuid),
            loadStart,
            asOfTimestamp.AddTicks(1),
            cancellationToken);

        loaded = loaded.Where(t => t.Timestamp >= loadStart && t.Timestamp <= asOfTimestamp).ToList();

        var triggering = loaded.FirstOrDefault(t => t.Timestamp == asOfTimestamp)
                         ?? loaded.OrderByDescending(t => t.Timestamp).FirstOrDefault();
        var currency = triggering?.Amount.Currency ?? "";
        var txs = loaded.Where(t => t.Amount.Currency == currency).ToList();

        var day = Since(txs, asOfTimestamp - Day);
        var hour = Since(txs, asOfTimestamp - Hour);
        var windowed = Since(txs, asOfTimestamp - window);

        var features = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["currency"] = currency,
            ["counterparty_country"] = CountryOf(triggering),
            ["transaction_amount"] = triggering?.Amount.Amount ?? 0m,
            ["transaction_type"] = triggering?.TransactionType.ToString() ?? "",
            ["direction"] = triggering?.Direction.ToString() ?? "",
            ["channel"] = triggering?.Channel.ToString() ?? ""
        };
        AddAggregates(features, day, "_24h", includeFlow: false);
        AddAggregates(features, hour, "_1h", includeFlow: true, includeTotals: false);
        AddAggregates(features, windowed, "", includeFlow: true);
        AddDayPatterns(features, day);
        AddHistory(features, windowed, triggering, asOfTimestamp, window);

        var ids = windowed.Select(t => t.Id.ToString()).ToList();
        if (triggering is not null && !ids.Contains(triggering.Id.ToString()))
        {
            ids.Add(triggering.Id.ToString());
        }

        return new FeatureCalculationResult(features, ids);
    }

    private static List<CanonicalTransaction> Since(IEnumerable<CanonicalTransaction> txs, DateTimeOffset start)
        => txs.Where(t => t.Timestamp >= start).ToList();

    private static string CountryOf(CanonicalTransaction? tx) => tx?.CounterpartyCountry?.Trim().ToUpperInvariant() ?? "";

    /// <summary>Round amounts are exact multiples of 10,000 in the transaction currency.</summary>
    private static bool IsRound(decimal amount) => amount >= 10_000m && amount % 10_000m == 0m;

    private static void AddDayPatterns(IDictionary<string, object> features, IReadOnlyList<CanonicalTransaction> day)
    {
        var cash = day.Where(t => t.TransactionType == TransactionType.CASH_DEPOSIT).ToList();
        features["cash_deposit_count_24h"] = cash.Count;
        features["cash_deposit_sum_24h"] = cash.Sum(t => t.Amount.Amount);
        features["cash_deposit_max_24h"] = cash.Count == 0 ? 0m : cash.Max(t => t.Amount.Amount);
        features["round_amount_count_24h"] = day.Count(t => IsRound(t.Amount.Amount));
        features["distinct_account_count_24h"] = day.Select(t => t.AccountId).Distinct().Count();
    }

    /// <summary>
    /// Gaps and baselines within the rule window. With no earlier activity the gap features equal
    /// the window length, so "dormant for N days" rules need a window of at least N days.
    /// </summary>
    private static void AddHistory(
        IDictionary<string, object> features,
        IReadOnlyList<CanonicalTransaction> windowed,
        CanonicalTransaction? triggering,
        DateTimeOffset asOf,
        TimeSpan window)
    {
        var windowDays = Days(window);
        features["distinct_counterparty_country_count"] = windowed
            .Select(CountryOf).Where(c => c.Length > 0).Distinct().Count();

        var earlier = windowed.Where(t => t.Timestamp <= asOf && !ReferenceEquals(t, triggering)).ToList();
        features["days_since_previous_transaction"] = earlier.Count == 0
            ? windowDays
            : Days(asOf - earlier.Max(t => t.Timestamp));
        features["days_since_first_transaction"] = windowed.Count == 0 ? 0m : Days(asOf - windowed.Min(t => t.Timestamp));

        var dayStart = asOf - Day;
        var beforeDay = windowed.Where(t => t.Timestamp < dayStart).ToList();
        var burstStart = windowed.Where(t => t.Timestamp >= dayStart).Select(t => t.Timestamp).DefaultIfEmpty(asOf).Min();
        features["dormant_days_before_24h"] = beforeDay.Count == 0
            ? windowDays
            : Days(burstStart - beforeDay.Max(t => t.Timestamp));

        var baselineDays = windowDays - 1m;
        var baseline = baselineDays <= 0m ? 0m : beforeDay.Sum(t => t.Amount.Amount) / baselineDays;
        var lastDay = windowed.Where(t => t.Timestamp >= dayStart).Sum(t => t.Amount.Amount);
        features["daily_average_sum"] = Math.Round(baseline, 2);
        features["sum_24h_vs_daily_average"] = baseline <= 0m ? 0m : Math.Round(lastDay / baseline, 2);
    }

    private static decimal Days(TimeSpan span) => Math.Round((decimal)span.TotalDays, 2);

    private static void AddAggregates(
        IDictionary<string, object> features,
        IReadOnlyList<CanonicalTransaction> txs,
        string suffix,
        bool includeFlow,
        bool includeTotals = true)
    {
        if (includeTotals)
        {
            features[$"transaction_count{suffix}"] = txs.Count;
            features[$"transaction_sum{suffix}"] = txs.Sum(t => t.Amount.Amount);
            features[$"max_single_amount{suffix}"] = txs.Count == 0 ? 0m : txs.Max(t => t.Amount.Amount);
        }

        if (!includeFlow) return;

        var credit = txs.Where(t => t.Direction == TransactionDirection.CREDIT).Sum(t => t.Amount.Amount);
        var debit = txs.Where(t => t.Direction == TransactionDirection.DEBIT).Sum(t => t.Amount.Amount);
        features[$"credit_sum{suffix}"] = credit;
        features[$"debit_sum{suffix}"] = debit;
        features[$"pass_through_ratio{suffix}"] = credit <= 0 ? 0m : Math.Min(1m, debit / credit);
    }
}
