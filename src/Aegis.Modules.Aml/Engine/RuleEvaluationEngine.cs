using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aegis.Modules.Aml.Domain;

namespace Aegis.Modules.Aml.Engine;

/// <summary>
/// Evaluates a versioned AML rule definition against a feature context.
/// This is the core engine that powers all AML detection.
/// </summary>
public sealed class RuleEvaluationEngine : IRuleEvaluationEngine
{
    private readonly ConditionGroupEvaluator _groupEvaluator;
    private readonly ExclusionEvaluator _exclusionEvaluator;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RuleEvaluationEngine(
        ConditionGroupEvaluator groupEvaluator,
        ExclusionEvaluator exclusionEvaluator)
    {
        _groupEvaluator   = groupEvaluator;
        _exclusionEvaluator = exclusionEvaluator;
    }

    public Task<RuleEvaluationResult> EvaluateAsync(
        AmlRuleVersion ruleVersion,
        IFeatureContext features,
        string focusEntityId,
        string focusEntityType,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // ── Deserialize definition ────────────────────────────────────────
            RuleDefinition? definition;
            try
            {
                definition = JsonSerializer.Deserialize<RuleDefinition>(
                    ruleVersion.DefinitionJson, JsonOptions);
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(ruleVersion, focusEntityId, focusEntityType, stopwatch,
                    $"Rule definition JSON is invalid: {ex.Message}"));
            }

            if (definition is null)
                return Task.FromResult(Fail(ruleVersion, focusEntityId, focusEntityType, stopwatch,
                    "Rule definition deserialized to null"));

            // ── Check exclusions first ────────────────────────────────────────
            var exclusionResult = _exclusionEvaluator.Evaluate(definition.Exclusions, features);
            if (exclusionResult.IsExcluded)
            {
                stopwatch.Stop();
                return Task.FromResult(new RuleEvaluationResult
                {
                    RuleId             = ruleVersion.RuleId,
                    RuleVersionId      = ruleVersion.Id,
                    RuleCode           = ruleVersion.RuleCode,
                    RuleName           = ruleVersion.RuleName,
                    RuleVersion        = ruleVersion.VersionNumber,
                    FocusEntityId      = focusEntityId,
                    FocusEntityType    = focusEntityType,
                    IsTriggered        = false,
                    IsExcluded         = true,
                    ExclusionReason    = exclusionResult.ExclusionReason,
                    ConditionResults   = new List<ConditionEvaluationResult>(),
                    EvaluatedFeatures  = features.GetAll(),
                    EvaluatedAt        = DateTimeOffset.UtcNow,
                    EvaluationDuration = stopwatch.Elapsed
                });
            }

            // ── Evaluate conditions ───────────────────────────────────────────
            var groupResult = _groupEvaluator.Evaluate(definition.Conditions, features);

            DetectionEvidence? evidence = null;
            if (groupResult.Satisfied)
            {
                evidence = new DetectionEvidence
                {
                    RuleName            = ruleVersion.RuleName,
                    RuleVersionNumber   = ruleVersion.VersionNumber,
                    EvaluatedValues     = features.GetAll().ToDictionary(k => k.Key, v => v.Value),
                    ConditionsSatisfied = groupResult.Results
                        .Where(r => r.Satisfied)
                        .Select(r => r.ConditionDescription)
                        .ToList(),
                    TransactionIds    = new List<string>(),
                    AdditionalContext = new Dictionary<string, object>()
                };
            }

            stopwatch.Stop();
            return Task.FromResult(new RuleEvaluationResult
            {
                RuleId             = ruleVersion.RuleId,
                RuleVersionId      = ruleVersion.Id,
                RuleCode           = ruleVersion.RuleCode,
                RuleName           = ruleVersion.RuleName,
                RuleVersion        = ruleVersion.VersionNumber,
                FocusEntityId      = focusEntityId,
                FocusEntityType    = focusEntityType,
                IsTriggered        = groupResult.Satisfied,
                IsExcluded         = false,
                ExclusionReason    = null,
                ConditionResults   = groupResult.Results,
                EvaluatedFeatures  = features.GetAll(),
                EvaluatedAt        = DateTimeOffset.UtcNow,
                EvaluationDuration = stopwatch.Elapsed,
                Evidence           = evidence
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(Fail(ruleVersion, focusEntityId, focusEntityType, stopwatch, ex.Message));
        }
    }

    private RuleEvaluationResult Fail(
        AmlRuleVersion? ruleVersion,
        string focusEntityId,
        string focusEntityType,
        Stopwatch stopwatch,
        string error)
    {
        stopwatch.Stop();
        return new RuleEvaluationResult
        {
            RuleId             = ruleVersion?.RuleId ?? Guid.Empty,
            RuleVersionId      = ruleVersion?.Id ?? Guid.Empty,
            RuleCode           = ruleVersion?.RuleCode ?? "UNKNOWN",
            RuleName           = ruleVersion?.RuleName ?? "UNKNOWN",
            RuleVersion        = ruleVersion?.VersionNumber ?? 0,
            FocusEntityId      = focusEntityId,
            FocusEntityType    = focusEntityType,
            IsTriggered        = false,
            IsExcluded         = false,
            ExclusionReason    = $"Evaluation failed: {error}",
            ConditionResults   = new List<ConditionEvaluationResult>(),
            EvaluatedFeatures  = new Dictionary<string, object>(),
            EvaluatedAt        = DateTimeOffset.UtcNow,
            EvaluationDuration = stopwatch.Elapsed
        };
    }
}
