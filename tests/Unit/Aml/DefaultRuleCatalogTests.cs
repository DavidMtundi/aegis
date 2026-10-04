namespace Aegis.Tests.Unit.Aml;

using Aegis.Modules.Aml.Application;
using Aegis.Modules.Aml.Domain;
using Aegis.Modules.Aml.Engine;
using Aegis.Modules.Features.Application;
using Aegis.Modules.Transactions.Application;
using Aegis.Modules.Transactions.Domain;
using Aegis.Shared.Domain;

/// <summary>Each default rule fires on its typology and stays quiet on a near miss, end to end through features and the engine.</summary>
public sealed class DefaultRuleCatalogTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CustomerId Customer = new(Guid.NewGuid());
    private static readonly DateTimeOffset AsOf = DateTimeOffset.Parse("2026-09-23T12:00:00Z");

    private sealed class StubReadPort(IReadOnlyList<CanonicalTransaction> txs) : ITransactionReadPort
    {
        public Task<IReadOnlyList<CanonicalTransaction>> GetForCustomerWindowAsync(
            TenantId tenantId, CustomerId customerId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CanonicalTransaction>>(
                txs.Where(t => t.Timestamp >= start && t.Timestamp < end).OrderBy(t => t.Timestamp).ToList());
    }

    private static int _ref;

    private static CanonicalTransaction Tx(
        TimeSpan ago,
        decimal amount,
        TransactionDirection direction = TransactionDirection.CREDIT,
        TransactionType type = TransactionType.TRANSFER,
        string? country = null,
        AccountId? account = null)
        => CanonicalTransaction.Ingest(Tenant, $"T-{Interlocked.Increment(ref _ref)}", account ?? Wallet, Customer,
            AsOf - ago, new Money(amount, "KES"), direction, type, TransactionChannel.MOBILE, country);

    private static readonly AccountId Wallet = new(Guid.NewGuid());
    private static TimeSpan H(double h) => TimeSpan.FromHours(h);
    private static TimeSpan D(double d) => TimeSpan.FromDays(d);
    private static readonly TimeSpan Now = TimeSpan.Zero;

    private static IEnumerable<CanonicalTransaction> Repeat(int n, Func<int, CanonicalTransaction> make)
        => Enumerable.Range(0, n).Select(make);

    private static async Task<bool> FiresAsync(string code, IEnumerable<CanonicalTransaction> txs)
    {
        var rule = DefaultRuleCatalog.All.Single(r => r.Code == code);
        Assert.True(RuleDuration.TryParse(rule.Definition.Schedule.Lookback, out var window));
        var features = await new FeatureCalculator(new StubReadPort(txs.ToList()))
            .CalculateAsync(Tenant, FocusType.CUSTOMER, Customer.Value.ToString(), AsOf, window);
        var cond = new ConditionEvaluator();
        var engine = new RuleEvaluationEngine(new ConditionGroupEvaluator(cond), new ExclusionEvaluator(cond));
        var version = AmlRuleVersion.CreateActive(Guid.NewGuid(), Tenant, 1, rule.Definition, "test", AsOf);
        var result = await engine.EvaluateAsync(version, new DictionaryFeatureContext(features.Features.ToDictionary(k => k.Key, v => v.Value)),
            Customer.Value.ToString(), "CUSTOMER");
        return result.IsTriggered;
    }

    public static TheoryData<string, CanonicalTransaction[], CanonicalTransaction[]> Typologies()
    {
        var a2 = new AccountId(Guid.NewGuid());
        var a3 = new AccountId(Guid.NewGuid());
        return new()
        {
            { "STRUCTURING_001",
                Repeat(5, i => Tx(H(i), 95_000m)).ToArray(),
                Repeat(4, i => Tx(H(i), 95_000m)).ToArray() },
            { "RAPID_MOVEMENT_001",
                [Tx(H(0.5), 100_000m), Tx(Now, 95_000m, TransactionDirection.DEBIT)],
                [Tx(H(0.5), 100_000m), Tx(Now, 40_000m, TransactionDirection.DEBIT)] },
            { "HIGH_RISK_GEOGRAPHY_001",
                [Tx(Now, 15_000m, country: "KP")],
                [Tx(Now, 15_000m, country: "AE")] },
            { "STRUCTURING_CASH_001",
                Repeat(3, i => Tx(H(i), 90_000m, type: TransactionType.CASH_DEPOSIT)).ToArray(),
                Repeat(3, i => Tx(H(i), 90_000m)).ToArray() },
            { "STRUCTURING_7D_001",
                Repeat(12, i => Tx(H(i * 12), 80_000m)).ToArray(),
                Repeat(8, i => Tx(H(i * 12), 80_000m)).ToArray() },
            { "LARGE_CASH_001",
                [Tx(Now, 1_200_000m, TransactionDirection.DEBIT, TransactionType.CASH_WITHDRAWAL)],
                [Tx(Now, 1_200_000m, TransactionDirection.DEBIT)] },
            { "LARGE_VALUE_001",
                [Tx(Now, 6_000_000m)],
                [Tx(Now, 4_000_000m)] },
            { "RAPID_MOVEMENT_24H_001",
                [Tx(H(10), 600_000m), Tx(Now, 570_000m, TransactionDirection.DEBIT)],
                [Tx(H(10), 600_000m), Tx(Now, 300_000m, TransactionDirection.DEBIT)] },
            { "VELOCITY_24H_001",
                Repeat(20, i => Tx(H(i), 500m)).ToArray(),
                Repeat(10, i => Tx(H(i), 500m)).ToArray() },
            { "ROUND_AMOUNTS_001",
                Repeat(4, i => Tx(H(i), 60_000m)).ToArray(),
                Repeat(4, i => Tx(H(i), 61_500m)).ToArray() },
            { "DORMANT_REACTIVATION_001",
                [Tx(D(75), 2_000m), Tx(Now, 150_000m)],
                [Tx(D(20), 2_000m), Tx(Now, 150_000m)] },
            { "DORMANT_SPIKE_001",
                [Tx(D(70), 2_000m), .. Repeat(3, i => Tx(H(i * 2), 100_000m))],
                [Tx(D(10), 2_000m), .. Repeat(3, i => Tx(H(i * 2), 100_000m))] },
            { "NEW_ACCOUNT_FLIGHT_001",
                [Tx(D(5), 600_000m), Tx(Now, 550_000m, TransactionDirection.DEBIT)],
                [Tx(D(25), 600_000m), Tx(Now, 550_000m, TransactionDirection.DEBIT)] },
            { "MULTI_ACCOUNT_001",
                [Tx(H(3), 120_000m), Tx(H(2), 120_000m, account: a2), Tx(Now, 120_000m, account: a3)],
                Repeat(3, i => Tx(H(i), 120_000m)).ToArray() },
            { "GEO_SPREAD_7D_001",
                [Tx(D(5), 5_000m, country: "AE"), Tx(D(3), 5_000m, country: "CN"), Tx(D(1), 5_000m, country: "GB"), Tx(Now, 5_000m, country: "IN")],
                [Tx(D(3), 5_000m, country: "CN"), Tx(D(1), 5_000m, country: "GB"), Tx(Now, 5_000m, country: "IN")] },
            { "BEHAVIOUR_SPIKE_001",
                [.. Repeat(29, i => Tx(D(i + 2), 2_000m)), Tx(Now, 250_000m)],
                [Tx(Now, 250_000m)] },
        };
    }

    [Theory]
    [MemberData(nameof(Typologies))]
    public async Task Rule_fires_on_its_typology_and_not_on_a_near_miss(string code, CanonicalTransaction[] hit, CanonicalTransaction[] miss)
    {
        Assert.True(await FiresAsync(code, hit), $"{code} should fire");
        Assert.False(await FiresAsync(code, miss), $"{code} should not fire");
    }

    [Fact]
    public void Every_rule_has_a_typology_test()
    {
        var tested = Typologies().Select(row => (string)row[0]).ToHashSet();
        Assert.Equal(DefaultRuleCatalog.All.Select(r => r.Code).OrderBy(c => c), tested.OrderBy(c => c));
    }

    [Fact]
    public void Codes_are_unique_and_definitions_valid()
    {
        Assert.Equal(DefaultRuleCatalog.All.Count, DefaultRuleCatalog.All.Select(r => r.Code).Distinct().Count());
        var validator = new RuleDefinitionValidator();
        foreach (var rule in DefaultRuleCatalog.All)
        {
            var result = validator.Validate(rule.Definition);
            Assert.True(result.IsValid, $"{rule.Code}: {string.Join("; ", result.Errors)}");
            Assert.Equal(rule.Code, rule.Definition.Code);
        }
    }

    [Fact]
    public async Task Every_condition_reads_a_feature_the_calculator_produces()
    {
        foreach (var rule in DefaultRuleCatalog.All)
        {
            Assert.True(RuleDuration.TryParse(rule.Definition.Schedule.Lookback, out var window));
            var features = (await new FeatureCalculator(new StubReadPort([Tx(Now, 1_000m)]))
                .CalculateAsync(Tenant, FocusType.CUSTOMER, Customer.Value.ToString(), AsOf, window)).Features;
            foreach (var condition in rule.Definition.Conditions.All!)
                Assert.True(features.ContainsKey(condition.Field), $"{rule.Code} reads unknown feature {condition.Field}");
        }
    }

    [Fact]
    public void Kes_thresholds_only_apply_to_kes_transactions()
    {
        foreach (var rule in DefaultRuleCatalog.All)
            Assert.Contains(rule.Definition.Conditions.All!, c => c.Field == "currency" && c.Values!.SequenceEqual(["KES"]));
    }
}
