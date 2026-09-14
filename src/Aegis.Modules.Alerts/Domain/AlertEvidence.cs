namespace Aegis.Modules.Alerts.Domain;

using System.Collections.Generic;

public sealed record AlertEvidence
{
    public string RuleName { get; init; } = string.Empty;
    public int RuleVersionNumber { get; init; }
    public Dictionary<string, object> EvaluatedValues { get; init; } = new();
    public List<string> ConditionsSatisfied { get; init; } = new();
    public List<string> TransactionIds { get; init; } = new();
    public Dictionary<string, object> AdditionalContext { get; init; } = new();
}
