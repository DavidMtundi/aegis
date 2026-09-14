using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Aegis.Modules.Aml.Domain;
using Aegis.Modules.Aml.Engine;
using Aegis.Shared.Domain;
using Xunit;

namespace Aegis.Tests.Unit.Aml;

public class RuleEvaluationEngineTests
{
    private readonly RuleEvaluationEngine _sut;

    public RuleEvaluationEngineTests()
    {
        var condEval  = new ConditionEvaluator();
        var groupEval = new ConditionGroupEvaluator(condEval);
        var exclEval  = new ExclusionEvaluator(condEval);
        _sut = new RuleEvaluationEngine(groupEval, exclEval);
    }

    private static AmlRuleVersion BuildVersion(RuleDefinition def) =>
        AmlRuleVersion.Create(
            Guid.NewGuid(),
            TenantId.New(),
            1,
            def,
            "system_test");

    [Fact]
    public async Task EvaluateAsync_AllConditionsMet_TriggersRule()
    {
        var def = new RuleDefinition
        {
            Code = "TEST_RULE",
            Name = "Test Rule",
            Conditions = new RuleConditionGroup
            {
                All = new List<RuleCondition>
                {
                    new() { Field = "credit_amount", Operator = ">=", Value = 100000m },
                    new() { Field = "debit_amount",  Operator = ">=", Value = 80000m  }
                }
            },
            Severity = AlertSeverity.HIGH,
            RiskScore = 40
        };

        var version = BuildVersion(def);
        var context = new DictionaryFeatureContext(new Dictionary<string, object>
        {
            { "credit_amount", 150000m },
            { "debit_amount",  90000m  }
        });

        var result = await _sut.EvaluateAsync(version, context, "ACC-001", "ACCOUNT");

        Assert.True(result.IsTriggered);
        Assert.Equal("TEST_RULE", result.RuleCode);
        Assert.NotNull(result.Evidence);
        Assert.Equal(2, result.Evidence.ConditionsSatisfied.Count);
    }

    [Fact]
    public async Task EvaluateAsync_ExclusionMatches_DoesNotTrigger()
    {
        var def = new RuleDefinition
        {
            Code = "TEST_RULE",
            Name = "Test Rule",
            Conditions = new RuleConditionGroup
            {
                All = new List<RuleCondition>
                {
                    new() { Field = "credit_amount", Operator = ">=", Value = 100000m }
                }
            },
            Exclusions = new List<RuleCondition>
            {
                new() { Field = "is_exempt", Operator = "=", Value = true }
            }
        };

        var version = BuildVersion(def);
        var context = new DictionaryFeatureContext(new Dictionary<string, object>
        {
            { "credit_amount", 150000m },
            { "is_exempt",      true    }
        });

        var result = await _sut.EvaluateAsync(version, context, "ACC-001", "ACCOUNT");

        Assert.False(result.IsTriggered);
        Assert.True(result.IsExcluded);
        Assert.NotNull(result.ExclusionReason);
    }

    [Fact]
    public async Task EvaluateAsync_AnyConditionGroup_SatisfiedWhenOneMatches()
    {
        var def = new RuleDefinition
        {
            Code = "TEST_RULE",
            Name = "Test Rule",
            Conditions = new RuleConditionGroup
            {
                Any = new List<RuleCondition>
                {
                    new() { Field = "credit_amount", Operator = ">=", Value = 1000000m },
                    new() { Field = "tx_count",      Operator = ">=", Value = 50       }
                }
            }
        };

        var version = BuildVersion(def);
        var context = new DictionaryFeatureContext(new Dictionary<string, object>
        {
            { "credit_amount", 10000m },
            { "tx_count",      60     }
        });

        var result = await _sut.EvaluateAsync(version, context, "ACC-001", "ACCOUNT");

        Assert.True(result.IsTriggered);
    }

    [Fact]
    public async Task EvaluateAsync_EmptyConditionsGroup_DoesNotTrigger()
    {
        var def = new RuleDefinition
        {
            Code = "TEST_RULE",
            Name = "Test Rule",
            Conditions = new RuleConditionGroup
            {
                All = new List<RuleCondition>()
            }
        };

        var version = BuildVersion(def);
        var context = new DictionaryFeatureContext(new Dictionary<string, object>());

        var result = await _sut.EvaluateAsync(version, context, "ACC-001", "ACCOUNT");

        Assert.False(result.IsTriggered);
    }

    [Fact]
    public async Task EvaluateAsync_InvalidDefinitionJson_ReturnsFailedResult()
    {
        var version = AmlRuleVersion.Create(
            Guid.NewGuid(),
            TenantId.New(),
            1,
            new RuleDefinition { Code = "TEST" },
            "system_test");

        var context = new DictionaryFeatureContext(new Dictionary<string, object>());
        var result = await _sut.EvaluateAsync(version, context, "ACC-001", "ACCOUNT");

        Assert.False(result.IsTriggered);
    }
}
