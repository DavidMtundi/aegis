namespace Aegis.Tests.Unit.Aml;

using Aegis.Modules.Aml.Domain;
using Aegis.Modules.Aml.Engine;
using Aegis.Shared.Domain;

public sealed class StructuringRuleEvaluationTests
{
    private readonly RuleEvaluationEngine _engine;

    public StructuringRuleEvaluationTests()
    {
        var cond = new ConditionEvaluator();
        _engine = new RuleEvaluationEngine(new ConditionGroupEvaluator(cond), new ExclusionEvaluator(cond));
    }

    private static AmlRuleVersion StructuringVersion()
    {
        var def = new RuleDefinition
        {
            Code = "STRUCTURING_001",
            Name = "Structuring",
            Focus = FocusType.CUSTOMER,
            Schedule = new RuleSchedule { Frequency = "realtime", Lookback = "24h" },
            Conditions = new RuleConditionGroup
            {
                All = new List<RuleCondition>
                {
                    new() { Field = "transaction_count_24h", Operator = ">=", Value = 5 },
                    new() { Field = "transaction_sum_24h", Operator = ">=", Value = 450000m },
                    new() { Field = "max_single_amount_24h", Operator = "<", Value = 100000m }
                }
            },
            Severity = AlertSeverity.HIGH,
            RiskScore = 80,
            Actions = new List<string> { "CREATE_ALERT" }
        };

        return AmlRuleVersion.CreateActive(
            Guid.NewGuid(),
            new TenantId(Guid.NewGuid()),
            1,
            def,
            "system-seed",
            DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Seven_x_95k_triggers_via_generic_all_AND()
    {
        var features = new DictionaryFeatureContext(new Dictionary<string, object>
        {
            ["transaction_count_24h"] = 7,
            ["transaction_sum_24h"] = 665_000m,
            ["max_single_amount_24h"] = 95_000m
        });

        var result = await _engine.EvaluateAsync(
            StructuringVersion(), features, Guid.NewGuid().ToString(), FocusType.CUSTOMER.ToString());

        Assert.True(result.IsTriggered);
        Assert.Equal("STRUCTURING_001", result.RuleCode);
        Assert.Equal(3, result.ConditionResults.Count);
        Assert.All(result.ConditionResults, c => Assert.True(c.Satisfied));
    }

    [Fact]
    public async Task Four_x_95k_does_not_trigger_due_to_count()
    {
        var features = new DictionaryFeatureContext(new Dictionary<string, object>
        {
            ["transaction_count_24h"] = 4,
            ["transaction_sum_24h"] = 380_000m,
            ["max_single_amount_24h"] = 95_000m
        });

        var result = await _engine.EvaluateAsync(
            StructuringVersion(), features, Guid.NewGuid().ToString(), FocusType.CUSTOMER.ToString());

        Assert.False(result.IsTriggered);
        Assert.Contains(result.ConditionResults, c => !c.Satisfied && c.ConditionDescription.Contains("transaction_count_24h"));
    }

    [Fact]
    public async Task Five_x_90k_triggers_at_sum_boundary()
    {
        var features = new DictionaryFeatureContext(new Dictionary<string, object>
        {
            ["transaction_count_24h"] = 5,
            ["transaction_sum_24h"] = 450_000m,
            ["max_single_amount_24h"] = 90_000m
        });

        var result = await _engine.EvaluateAsync(
            StructuringVersion(), features, Guid.NewGuid().ToString(), FocusType.CUSTOMER.ToString());

        Assert.True(result.IsTriggered);
    }

    [Fact]
    public async Task Five_x_100k_does_not_trigger_due_to_max_single()
    {
        var features = new DictionaryFeatureContext(new Dictionary<string, object>
        {
            ["transaction_count_24h"] = 5,
            ["transaction_sum_24h"] = 500_000m,
            ["max_single_amount_24h"] = 100_000m
        });

        var result = await _engine.EvaluateAsync(
            StructuringVersion(), features, Guid.NewGuid().ToString(), FocusType.CUSTOMER.ToString());

        Assert.False(result.IsTriggered);
        Assert.Contains(result.ConditionResults, c => !c.Satisfied && c.ConditionDescription.Contains("max_single_amount_24h"));
    }
}
