namespace Aegis.Modules.Features.Application;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aegis.Modules.Transactions.Application;
using Aegis.Modules.Transactions.Domain;
using Aegis.Shared.Domain;

public sealed class FeatureCalculator : IFeatureCalculator
{
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

        var customerId = new CustomerId(customerGuid);
        // Load the wider of requested window and 24h so we can compute both structuring and rapid features.
        var loadWindow = window > TimeSpan.FromHours(24) ? window : TimeSpan.FromHours(24);
        var loadStart = asOfTimestamp - loadWindow;
        var txs = await _transactions.GetForCustomerWindowAsync(
            tenantId,
            customerId,
            loadStart,
            asOfTimestamp.AddTicks(1),
            cancellationToken);

        txs = txs.Where(t => t.Timestamp >= loadStart && t.Timestamp <= asOfTimestamp).ToList();

        var window24Start = asOfTimestamp - TimeSpan.FromHours(24);
        var txs24 = txs.Where(t => t.Timestamp >= window24Start).ToList();
        var count = txs24.Count;
        var sum = txs24.Sum(t => t.Amount.Amount);
        var max = txs24.Count == 0 ? 0m : txs24.Max(t => t.Amount.Amount);

        var window1Start = asOfTimestamp - TimeSpan.FromHours(1);
        var txs1h = txs.Where(t => t.Timestamp >= window1Start).ToList();
        var creditSum1h = txs1h.Where(t => t.Direction == TransactionDirection.CREDIT).Sum(t => t.Amount.Amount);
        var debitSum1h = txs1h.Where(t => t.Direction == TransactionDirection.DEBIT).Sum(t => t.Amount.Amount);
        var passThrough = creditSum1h <= 0 ? 0m : Math.Min(1m, debitSum1h / creditSum1h);

        var triggering = txs.FirstOrDefault(t => t.Timestamp == asOfTimestamp)
                         ?? txs.OrderByDescending(t => t.Timestamp).FirstOrDefault();
        var counterpartyCountry = triggering?.CounterpartyCountry?.Trim().ToUpperInvariant() ?? "";
        var transactionAmount = triggering?.Amount.Amount ?? 0m;

        var ids = txs24.Select(t => t.Id.ToString()).ToList();
        if (triggering is not null && !ids.Contains(triggering.Id.ToString()))
        {
            ids.Add(triggering.Id.ToString());
        }

        var features = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["transaction_count_24h"] = count,
            ["transaction_sum_24h"] = sum,
            ["max_single_amount_24h"] = max,
            ["credit_sum_1h"] = creditSum1h,
            ["debit_sum_1h"] = debitSum1h,
            ["pass_through_ratio_1h"] = passThrough,
            ["counterparty_country"] = counterpartyCountry,
            ["transaction_amount"] = transactionAmount
        };

        return new FeatureCalculationResult(features, ids);
    }
}
