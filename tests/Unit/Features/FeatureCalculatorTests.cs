namespace Aegis.Tests.Unit.Features;

using Aegis.Modules.Features.Application;
using Aegis.Modules.Transactions.Application;
using Aegis.Modules.Transactions.Domain;
using Aegis.Shared.Domain;

public sealed class FeatureCalculatorTests
{
    private sealed class StubReadPort : ITransactionReadPort
    {
        private readonly List<CanonicalTransaction> _txs;

        public StubReadPort(IEnumerable<CanonicalTransaction> txs) => _txs = txs.ToList();

        public Task<IReadOnlyList<CanonicalTransaction>> GetForCustomerWindowAsync(
            TenantId tenantId,
            CustomerId customerId,
            DateTimeOffset windowStartInclusive,
            DateTimeOffset windowEndExclusive,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CanonicalTransaction>>(
                _txs.Where(t =>
                        t.TenantId == tenantId
                        && t.CustomerId == customerId
                        && t.Timestamp >= windowStartInclusive
                        && t.Timestamp < windowEndExclusive)
                    .OrderBy(t => t.Timestamp)
                    .ToList());
    }

    private static CanonicalTransaction Tx(
        TenantId tenantId,
        CustomerId customerId,
        DateTimeOffset ts,
        decimal amount,
        string extRef)
        => CanonicalTransaction.Ingest(
            tenantId,
            extRef,
            new AccountId(Guid.NewGuid()),
            customerId,
            ts,
            new Money(amount, "KES"),
            TransactionDirection.CREDIT,
            TransactionType.TRANSFER,
            TransactionChannel.MOBILE);

    [Fact]
    public async Task Features_for_seven_95k_match_thresholds()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var customerId = new CustomerId(Guid.NewGuid());
        var asOf = DateTimeOffset.UtcNow.AddMinutes(-1);
        var txs = Enumerable.Range(0, 7)
            .Select(i => Tx(tenantId, customerId, asOf.AddMinutes(-i), 95_000m, $"TX-{i}"))
            .ToList();

        var calc = new FeatureCalculator(new StubReadPort(txs));
        var result = await calc.CalculateAsync(
            tenantId, FocusType.CUSTOMER, customerId.Value.ToString(), asOf, TimeSpan.FromHours(24));

        Assert.Equal(7, Convert.ToInt32(result.Features["transaction_count_24h"]));
        Assert.Equal(665_000m, Convert.ToDecimal(result.Features["transaction_sum_24h"]));
        Assert.Equal(95_000m, Convert.ToDecimal(result.Features["max_single_amount_24h"]));
        Assert.Equal(7, result.TransactionIds.Count);
    }

    [Fact]
    public async Task Four_times_95k_count_below_structuring_threshold()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var customerId = new CustomerId(Guid.NewGuid());
        var asOf = DateTimeOffset.UtcNow.AddMinutes(-1);
        var txs = Enumerable.Range(0, 4)
            .Select(i => Tx(tenantId, customerId, asOf.AddMinutes(-i), 95_000m, $"TX-{i}"))
            .ToList();

        var calc = new FeatureCalculator(new StubReadPort(txs));
        var result = await calc.CalculateAsync(
            tenantId, FocusType.CUSTOMER, customerId.Value.ToString(), asOf, TimeSpan.FromHours(24));

        Assert.Equal(4, Convert.ToInt32(result.Features["transaction_count_24h"]));
        Assert.True(Convert.ToInt32(result.Features["transaction_count_24h"]) < 5);
    }

    [Fact]
    public async Task Boundary_five_times_90k_sum_at_threshold()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var customerId = new CustomerId(Guid.NewGuid());
        var asOf = DateTimeOffset.UtcNow.AddMinutes(-1);
        var txs = Enumerable.Range(0, 5)
            .Select(i => Tx(tenantId, customerId, asOf.AddMinutes(-i), 90_000m, $"TX-{i}"))
            .ToList();

        var calc = new FeatureCalculator(new StubReadPort(txs));
        var result = await calc.CalculateAsync(
            tenantId, FocusType.CUSTOMER, customerId.Value.ToString(), asOf, TimeSpan.FromHours(24));

        Assert.Equal(5, Convert.ToInt32(result.Features["transaction_count_24h"]));
        Assert.Equal(450_000m, Convert.ToDecimal(result.Features["transaction_sum_24h"]));
        Assert.Equal(90_000m, Convert.ToDecimal(result.Features["max_single_amount_24h"]));
    }

    [Fact]
    public async Task Window_excludes_transactions_older_than_lookback_and_includes_asOf()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var customerId = new CustomerId(Guid.NewGuid());
        var asOf = DateTimeOffset.Parse("2026-09-23T12:00:00Z");
        var window = TimeSpan.FromHours(24);

        var txs = new[]
        {
            Tx(tenantId, customerId, asOf.AddHours(-24).AddSeconds(-1), 10_000m, "too-old"),
            Tx(tenantId, customerId, asOf.AddHours(-24), 20_000m, "boundary-start"),
            Tx(tenantId, customerId, asOf.AddHours(-12), 30_000m, "mid"),
            Tx(tenantId, customerId, asOf, 40_000m, "asof"),
            Tx(tenantId, customerId, asOf.AddSeconds(1), 50_000m, "after-asof")
        };

        var calc = new FeatureCalculator(new StubReadPort(txs));
        var result = await calc.CalculateAsync(
            tenantId, FocusType.CUSTOMER, customerId.Value.ToString(), asOf, window);

        // Inclusive [asOf-24h, asOf]: boundary-start, mid, asof (3). too-old and after-asof excluded.
        Assert.Equal(3, Convert.ToInt32(result.Features["transaction_count_24h"]));
        Assert.Equal(90_000m, Convert.ToDecimal(result.Features["transaction_sum_24h"]));
        Assert.Equal(40_000m, Convert.ToDecimal(result.Features["max_single_amount_24h"]));
    }
}
