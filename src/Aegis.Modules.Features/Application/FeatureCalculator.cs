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

        var windowStart = asOfTimestamp - window;
        var txs = await _transactions.GetForCustomerWindowAsync(
            tenantId,
            new CustomerId(customerGuid),
            windowStart,
            asOfTimestamp.AddTicks(1), // inclusive as-of
            cancellationToken);

        // Window is [asOf-24h, asOf] inclusive on business timestamps
        txs = txs.Where(t => t.Timestamp >= windowStart && t.Timestamp <= asOfTimestamp).ToList();

        var count = txs.Count;
        var sum = txs.Sum(t => t.Amount.Amount);
        var max = txs.Count == 0 ? 0m : txs.Max(t => t.Amount.Amount);
        var ids = txs.Select(t => t.Id.ToString()).ToList();

        var features = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["transaction_count_24h"] = count,
            ["transaction_sum_24h"] = sum,
            ["max_single_amount_24h"] = max
        };

        return new FeatureCalculationResult(features, ids);
    }
}
