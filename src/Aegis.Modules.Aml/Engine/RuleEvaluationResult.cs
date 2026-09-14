using System;
using System.Collections.Generic;

namespace Aegis.Modules.Aml.Engine;

public sealed record DetectionEvidence
{
    public string RuleName { get; init; } = string.Empty;
    public int RuleVersionNumber { get; init; }
    public Dictionary<string, object> EvaluatedValues { get; init; } = new();
    public List<string> ConditionsSatisfied { get; init; } = new();
    public List<string> TransactionIds { get; init; } = new();
    public Dictionary<string, object> AdditionalContext { get; init; } = new();
}

public sealed record RuleEvaluationResult
{
    public Guid RuleId { get; init; }
    public Guid RuleVersionId { get; init; }
    public string RuleCode { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;
    public int RuleVersion { get; init; }
    
    public string FocusEntityId { get; init; } = string.Empty;
    public string FocusEntityType { get; init; } = string.Empty;
    
    public bool IsTriggered { get; init; }
    public bool IsExcluded { get; init; }
    public string? ExclusionReason { get; init; }
    
    public List<ConditionEvaluationResult> ConditionResults { get; init; } = new();
    public IReadOnlyDictionary<string, object> EvaluatedFeatures { get; init; } = new Dictionary<string, object>();
    
    public DateTimeOffset EvaluatedAt { get; init; }
    public TimeSpan EvaluationDuration { get; init; }
    
    public DetectionEvidence? Evidence { get; init; }
}
