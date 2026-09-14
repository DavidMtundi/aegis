using System.Collections.Generic;
using Aegis.Modules.Aml.Domain;
using Aegis.Modules.Aml.Engine;
using Xunit;

namespace Aegis.Tests.Unit.Aml;

public class ConditionEvaluatorTests
{
    private readonly ConditionEvaluator _sut = new ConditionEvaluator();

    [Theory]
    [InlineData(">", 99999, 100000, false)]
    [InlineData(">", 100001, 100000, true)]
    [InlineData(">=", 99999, 100000, false)]
    [InlineData(">=", 100000, 100000, true)]
    [InlineData(">=", 100001, 100000, true)]
    [InlineData("<", 100001, 100000, false)]
    [InlineData("<", 99999, 100000, true)]
    [InlineData("<=", 100001, 100000, false)]
    [InlineData("<=", 100000, 100000, true)]
    [InlineData("<=", 99999, 100000, true)]
    [InlineData("=", 100000, 100000, true)]
    [InlineData("=", 99999, 100000, false)]
    [InlineData("!=", 100000, 100000, false)]
    [InlineData("!=", 99999, 100000, true)]
    public void Evaluate_NumericComparisons_ReturnsExpectedResult(string op, decimal actual, decimal expectedValue, bool expectedResult)
    {
        var context = new DictionaryFeatureContext(new Dictionary<string, object> { { "Amount", actual } });
        var condition = new RuleCondition { Field = "Amount", Operator = op, Value = expectedValue };

        var result = _sut.Evaluate(condition, context);

        Assert.Equal(expectedResult, result.Satisfied);
    }

    [Theory]
    [InlineData(89999, false)]
    [InlineData(90000, true)]
    [InlineData(95000, true)]
    [InlineData(99999, true)]
    [InlineData(100000, false)]
    public void Evaluate_BetweenOperator_ReturnsExpectedResult(decimal actual, bool expectedResult)
    {
        var context = new DictionaryFeatureContext(new Dictionary<string, object> { { "Amount", actual } });
        var condition = new RuleCondition { Field = "Amount", Operator = "BETWEEN", ValueFrom = 90000, ValueTo = 99999 };

        var result = _sut.Evaluate(condition, context);

        Assert.Equal(expectedResult, result.Satisfied);
    }

    [Fact]
    public void Evaluate_InOperator_ReturnsExpectedResult()
    {
        var condition = new RuleCondition { Field = "Type", Operator = "IN", Values = new List<object> { "EFT", "WIRE" } };
        
        Assert.True(_sut.Evaluate(condition, new DictionaryFeatureContext(new Dictionary<string, object> { { "Type", "EFT" } })).Satisfied);
        Assert.True(_sut.Evaluate(condition, new DictionaryFeatureContext(new Dictionary<string, object> { { "Type", "WIRE" } })).Satisfied);
        Assert.False(_sut.Evaluate(condition, new DictionaryFeatureContext(new Dictionary<string, object> { { "Type", "CASH" } })).Satisfied);
    }

    [Fact]
    public void Evaluate_NotInOperator_ReturnsExpectedResult()
    {
        var condition = new RuleCondition { Field = "Type", Operator = "NOT_IN", Values = new List<object> { "EFT", "WIRE" } };
        
        Assert.False(_sut.Evaluate(condition, new DictionaryFeatureContext(new Dictionary<string, object> { { "Type", "EFT" } })).Satisfied);
        Assert.False(_sut.Evaluate(condition, new DictionaryFeatureContext(new Dictionary<string, object> { { "Type", "WIRE" } })).Satisfied);
        Assert.True(_sut.Evaluate(condition, new DictionaryFeatureContext(new Dictionary<string, object> { { "Type", "CASH" } })).Satisfied);
    }

    [Theory]
    [InlineData("ACTIVE", "ACTIVE", true)]
    [InlineData("ACTIVE", "INACTIVE", false)]
    public void Evaluate_StringEquality_ReturnsExpectedResult(string actual, string expectedValue, bool expectedResult)
    {
        var context = new DictionaryFeatureContext(new Dictionary<string, object> { { "Status", actual } });
        var condition = new RuleCondition { Field = "Status", Operator = "=", Value = expectedValue };

        var result = _sut.Evaluate(condition, context);

        Assert.Equal(expectedResult, result.Satisfied);
    }

    [Fact]
    public void Evaluate_NullChecks_ReturnsExpectedResult()
    {
        var isNullCond = new RuleCondition { Field = "Field", Operator = "IS_NULL" };
        var isNotNullCond = new RuleCondition { Field = "Field", Operator = "IS_NOT_NULL" };

        var contextWithNull = new DictionaryFeatureContext(new Dictionary<string, object> { { "Field", null! } });
        var contextWithVal = new DictionaryFeatureContext(new Dictionary<string, object> { { "Field", "value" } });
        var contextWithoutFeature = new DictionaryFeatureContext(new Dictionary<string, object>());

        Assert.True(_sut.Evaluate(isNullCond, contextWithNull).Satisfied);
        Assert.True(_sut.Evaluate(isNullCond, contextWithoutFeature).Satisfied);
        Assert.False(_sut.Evaluate(isNullCond, contextWithVal).Satisfied);

        Assert.False(_sut.Evaluate(isNotNullCond, contextWithNull).Satisfied);
        Assert.False(_sut.Evaluate(isNotNullCond, contextWithoutFeature).Satisfied);
        Assert.True(_sut.Evaluate(isNotNullCond, contextWithVal).Satisfied);
    }
}
