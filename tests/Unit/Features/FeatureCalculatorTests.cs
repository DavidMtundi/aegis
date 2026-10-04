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
        string extRef,
        string currency = "KES",
        TransactionType type = TransactionType.TRANSFER,
        TransactionDirection direction = TransactionDirection.CREDIT,
        AccountId? account = null,
        string? counterpartyCountry = null)
        => CanonicalTransaction.Ingest(
            tenantId,
            extRef,
            account ?? new AccountId(Guid.NewGuid()),
            customerId,
            ts,
            new Money(amount, currency),
            direction,
            type,
            TransactionChannel.MOBILE,
            counterpartyCountry);

    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CustomerId Customer = new(Guid.NewGuid());
    private static readonly DateTimeOffset AsOf = DateTimeOffset.Parse("2026-09-23T12:00:00Z");

    private static async Task<IReadOnlyDictionary<string, object>> FeaturesAsync(TimeSpan window, params CanonicalTransaction[] txs)
        => (await new FeatureCalculator(new StubReadPort(txs)).CalculateAsync(
            Tenant, FocusType.CUSTOMER, Customer.Value.ToString(), AsOf, window)).Features;

    [Fact]
    public async Task Triggering_transaction_type_direction_and_channel_are_features()
    {
        var f = await FeaturesAsync(TimeSpan.FromHours(24),
            Tx(Tenant, Customer, AsOf, 5_000m, "t", type: TransactionType.CASH_DEPOSIT));

        Assert.Equal("CASH_DEPOSIT", f["transaction_type"]);
        Assert.Equal("CREDIT", f["direction"]);
        Assert.Equal("MOBILE", f["channel"]);
    }

    [Fact]
    public async Task Cash_deposit_features_only_count_cash_deposits_in_24_hours()
    {
        var f = await FeaturesAsync(TimeSpan.FromHours(24),
            Tx(Tenant, Customer, AsOf.AddHours(-25), 90_000m, "old", type: TransactionType.CASH_DEPOSIT),
            Tx(Tenant, Customer, AsOf.AddHours(-3), 40_000m, "transfer"),
            Tx(Tenant, Customer, AsOf.AddHours(-2), 95_000m, "cash-1", type: TransactionType.CASH_DEPOSIT),
            Tx(Tenant, Customer, AsOf, 85_000m, "cash-2", type: TransactionType.CASH_DEPOSIT));

        Assert.Equal(2, Convert.ToInt32(f["cash_deposit_count_24h"]));
        Assert.Equal(180_000m, Convert.ToDecimal(f["cash_deposit_sum_24h"]));
        Assert.Equal(95_000m, Convert.ToDecimal(f["cash_deposit_max_24h"]));
    }

    [Fact]
    public async Task Round_amounts_and_distinct_accounts_are_counted_over_24_hours()
    {
        var wallet = new AccountId(Guid.NewGuid());
        var f = await FeaturesAsync(TimeSpan.FromHours(24),
            Tx(Tenant, Customer, AsOf.AddHours(-4), 50_000m, "round-1", account: wallet),
            Tx(Tenant, Customer, AsOf.AddHours(-3), 120_000m, "round-2", account: wallet),
            Tx(Tenant, Customer, AsOf.AddHours(-2), 95_000m, "thousands-only"),
            Tx(Tenant, Customer, AsOf, 50_250m, "not-round"));

        Assert.Equal(2, Convert.ToInt32(f["round_amount_count_24h"]));
        Assert.Equal(3, Convert.ToInt32(f["distinct_account_count_24h"]));
    }

    [Fact]
    public async Task Distinct_counterparty_countries_cover_the_rule_window()
    {
        var f = await FeaturesAsync(TimeSpan.FromDays(7),
            Tx(Tenant, Customer, AsOf.AddDays(-8), 1_000m, "too-old", counterpartyCountry: "CN"),
            Tx(Tenant, Customer, AsOf.AddDays(-5), 1_000m, "ae", counterpartyCountry: "AE"),
            Tx(Tenant, Customer, AsOf.AddDays(-2), 1_000m, "gb", counterpartyCountry: "gb"),
            Tx(Tenant, Customer, AsOf.AddDays(-1), 1_000m, "gb-again", counterpartyCountry: "GB"),
            Tx(Tenant, Customer, AsOf, 1_000m, "domestic"));

        Assert.Equal(2, Convert.ToInt32(f["distinct_counterparty_country_count"]));
    }

    [Fact]
    public async Task Days_since_previous_and_first_transaction_measure_gaps_in_the_window()
    {
        var f = await FeaturesAsync(TimeSpan.FromDays(90),
            Tx(Tenant, Customer, AsOf.AddDays(-80), 1_000m, "first"),
            Tx(Tenant, Customer, AsOf.AddDays(-70), 1_000m, "previous"),
            Tx(Tenant, Customer, AsOf, 150_000m, "reactivation"));

        Assert.Equal(70m, Convert.ToDecimal(f["days_since_previous_transaction"]));
        Assert.Equal(80m, Convert.ToDecimal(f["days_since_first_transaction"]));
    }

    [Fact]
    public async Task Without_earlier_activity_the_gap_is_the_whole_window_and_first_seen_is_now()
    {
        var f = await FeaturesAsync(TimeSpan.FromDays(90), Tx(Tenant, Customer, AsOf, 1_000m, "only"));

        Assert.Equal(90m, Convert.ToDecimal(f["days_since_previous_transaction"]));
        Assert.Equal(0m, Convert.ToDecimal(f["days_since_first_transaction"]));
    }

    [Fact]
    public async Task Dormant_days_before_24h_measures_the_quiet_period_before_a_burst()
    {
        var f = await FeaturesAsync(TimeSpan.FromDays(90),
            Tx(Tenant, Customer, AsOf.AddDays(-65), 1_000m, "last-before"),
            Tx(Tenant, Customer, AsOf.AddHours(-5), 60_000m, "burst-1"),
            Tx(Tenant, Customer, AsOf.AddHours(-1), 60_000m, "burst-2"),
            Tx(Tenant, Customer, AsOf, 60_000m, "burst-3"));

        var dormant = Convert.ToDecimal(f["dormant_days_before_24h"]);
        Assert.InRange(dormant, 64.7m, 64.9m);
        Assert.Equal(0.04m, Convert.ToDecimal(f["days_since_previous_transaction"]));
    }

    [Fact]
    public async Task Daily_average_excludes_the_last_24_hours_and_drives_the_spike_ratio()
    {
        var history = Enumerable.Range(2, 29)
            .Select(d => Tx(Tenant, Customer, AsOf.AddDays(-d), 2_000m, $"h-{d}"));
        var f = await FeaturesAsync(TimeSpan.FromDays(30),
            history.Append(Tx(Tenant, Customer, AsOf, 100_000m, "spike")).ToArray());

        Assert.Equal(2_000m, Convert.ToDecimal(f["daily_average_sum"]));
        Assert.Equal(50m, Convert.ToDecimal(f["sum_24h_vs_daily_average"]));
    }

    [Fact]
    public async Task Spike_ratio_is_zero_without_a_baseline()
    {
        var f = await FeaturesAsync(TimeSpan.FromDays(30), Tx(Tenant, Customer, AsOf, 100_000m, "first"));

        Assert.Equal(0m, Convert.ToDecimal(f["daily_average_sum"]));
        Assert.Equal(0m, Convert.ToDecimal(f["sum_24h_vs_daily_average"]));
    }

    [Fact]
    public async Task Generic_features_cover_the_requested_window_while_fixed_names_keep_their_windows()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var customerId = new CustomerId(Guid.NewGuid());
        var asOf = DateTimeOffset.Parse("2026-09-23T12:00:00Z");
        var txs = new[]
        {
            Tx(tenantId, customerId, asOf.AddDays(-6), 10_000m, "six-days"),
            Tx(tenantId, customerId, asOf.AddDays(-8), 99_000m, "too-old"),
            Tx(tenantId, customerId, asOf.AddHours(-2), 20_000m, "two-hours"),
            Tx(tenantId, customerId, asOf, 30_000m, "asof")
        };

        var result = await new FeatureCalculator(new StubReadPort(txs)).CalculateAsync(
            tenantId, FocusType.CUSTOMER, customerId.Value.ToString(), asOf, TimeSpan.FromDays(7));

        Assert.Equal(3, Convert.ToInt32(result.Features["transaction_count"]));
        Assert.Equal(60_000m, Convert.ToDecimal(result.Features["transaction_sum"]));
        Assert.Equal(30_000m, Convert.ToDecimal(result.Features["max_single_amount"]));
        Assert.Equal(2, Convert.ToInt32(result.Features["transaction_count_24h"]));
        Assert.Equal(3, result.TransactionIds.Count);
    }

    [Fact]
    public async Task Amount_features_only_use_the_triggering_currency()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var customerId = new CustomerId(Guid.NewGuid());
        var asOf = DateTimeOffset.Parse("2026-09-23T12:00:00Z");
        var txs = new[]
        {
            Tx(tenantId, customerId, asOf.AddHours(-3), 500m, "usd", "USD"),
            Tx(tenantId, customerId, asOf.AddHours(-2), 20_000m, "kes-1"),
            Tx(tenantId, customerId, asOf, 30_000m, "kes-2")
        };

        var result = await new FeatureCalculator(new StubReadPort(txs)).CalculateAsync(
            tenantId, FocusType.CUSTOMER, customerId.Value.ToString(), asOf, TimeSpan.FromHours(24));

        Assert.Equal("KES", result.Features["currency"]);
        Assert.Equal(2, Convert.ToInt32(result.Features["transaction_count_24h"]));
        Assert.Equal(50_000m, Convert.ToDecimal(result.Features["transaction_sum_24h"]));
        Assert.Equal(50_000m, Convert.ToDecimal(result.Features["transaction_sum"]));
        Assert.DoesNotContain(txs[0].Id.ToString(), result.TransactionIds);
    }

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
