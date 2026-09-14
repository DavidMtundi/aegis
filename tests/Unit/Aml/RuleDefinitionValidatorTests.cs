using System.Collections.Generic;
using Aegis.Modules.Aml.Domain;
using Aegis.Modules.Aml.Engine;
using Xunit;

namespace Aegis.Tests.Unit.Aml;

public class RuleDefinitionValidatorTests
{
    private readonly RuleDefinitionValidator _sut = new();

    // ── Code validation ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_MissingCode_ReturnsError()
    {
        var def = CreateValidDefinition();
        var result = _sut.Validate(def with { Code = "" });

        Assert.False(result.IsValid);
        Assert.Contains("Code is required.", result.Errors);
    }

    [Fact]
    public void Validate_InvalidCodeWithSpaces_ReturnsError()
    {
        var def = CreateValidDefinition();
        var result = _sut.Validate(def with { Code = "INVALID CODE" });

        Assert.False(result.IsValid);
        Assert.Contains("Code must contain only alphanumeric characters and underscores.", result.Errors);
    }

    [Fact]
    public void Validate_ValidCode_NoError()
    {
        var def = CreateValidDefinition();
        var result = _sut.Validate(def with { Code = "RAPID_MOVEMENT_001" });

        Assert.True(result.IsValid);
    }

    // ── Name validation ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_MissingName_ReturnsError()
    {
        var def = CreateValidDefinition();
        var result = _sut.Validate(def with { Name = "" });

        Assert.False(result.IsValid);
        Assert.Contains("Name is required.", result.Errors);
    }

    // ── RiskScore validation ──────────────────────────────────────────────────

    [Theory]
    [InlineData(-1,  false)]
    [InlineData(0,   true)]
    [InlineData(50,  true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Validate_RiskScore_ValidatesCorrectly(int score, bool expectedValid)
    {
        var def    = CreateValidDefinition();
        var result = _sut.Validate(def with { RiskScore = score });

        Assert.Equal(expectedValid, result.IsValid);
    }

    // ── Operator validation ───────────────────────────────────────────────────

    [Fact]
    public void Validate_InvalidOperatorInCondition_ReturnsError()
    {
        var def = CreateValidDefinition();
        var defWithBadOp = def with
        {
            Conditions = new RuleConditionGroup
            {
                All = new List<RuleCondition>
                {
                    new() { Field = "credit_amount", Operator = "INVALID_OP", Value = 100000m }
                }
            }
        };

        var result = _sut.Validate(defWithBadOp);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("INVALID_OP"));
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidCompleteDefinition_ReturnsTrue()
    {
        var def    = CreateValidDefinition();
        var result = _sut.Validate(def);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static RuleDefinition CreateValidDefinition() => new()
    {
        Code       = "RULE_001",
        Name       = "Test Rule",
        RiskScore  = 50,
        Schedule   = new RuleSchedule { Frequency = "1d", Lookback = "30d" },
        Conditions = new RuleConditionGroup
        {
            All = new List<RuleCondition>
            {
                new() { Field = "credit_amount", Operator = ">=", Value = 100000m }
            }
        }
    };
}
