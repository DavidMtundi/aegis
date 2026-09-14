namespace Aegis.Modules.Aml.Domain;

using System.Collections.Generic;
using Aegis.Shared.Domain;

public sealed record RuleDefinition
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public FocusType Focus { get; init; }
    public RuleSchedule Schedule { get; init; } = null!;
    public TransactionFilters? Filters { get; init; }
    public RuleConditionGroup Conditions { get; init; } = null!;
    public List<RuleCondition> Exclusions { get; init; } = new();
    public AlertSeverity Severity { get; init; }
    public int RiskScore { get; init; }
    public List<string> Actions { get; init; } = new();
}

public sealed record RuleSchedule
{
    public string Frequency { get; init; } = string.Empty;
    public string Lookback { get; init; } = string.Empty;
}

public sealed record TransactionFilters
{
    public List<string>? TransactionTypes { get; init; }
    public List<string>? Statuses { get; init; }
    public List<string>? Channels { get; init; }
    public string? Direction { get; init; }
}

public sealed record RuleConditionGroup
{
    public List<RuleCondition>? All { get; init; }
    public List<RuleCondition>? Any { get; init; }
    public List<RuleConditionGroup>? Groups { get; init; }
}

public sealed record RuleCondition
{
    public string Field { get; init; } = string.Empty;
    public string Operator { get; init; } = string.Empty;
    public object? Value { get; init; }
    public object? ValueFrom { get; init; }
    public object? ValueTo { get; init; }
    public List<object>? Values { get; init; }
}

