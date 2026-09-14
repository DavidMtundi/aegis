using System.Collections.Generic;
using System.Linq;
using Aegis.Modules.Aml.Domain;

namespace Aegis.Modules.Aml.Engine;

/// <summary>Result of evaluating a condition group.</summary>
public record GroupEvaluationResult(bool Satisfied, List<ConditionEvaluationResult> Results);

/// <summary>
/// Evaluates a <see cref="RuleConditionGroup"/> (AND / OR / nested) against a feature context.
/// Uses the domain model defined in Aegis.Modules.Aml.Domain.
/// </summary>
public sealed class ConditionGroupEvaluator
{
    private readonly ConditionEvaluator _conditionEvaluator;

    public ConditionGroupEvaluator(ConditionEvaluator conditionEvaluator)
    {
        _conditionEvaluator = conditionEvaluator;
    }

    /// <summary>
    /// Evaluates a rule condition group.
    /// - If <c>All</c> is set → AND logic (every condition must pass).
    /// - If <c>Any</c> is set → OR logic (at least one condition must pass).
    /// - Empty <c>All</c> list → NOT satisfied (prevents vacuous mass alerting).
    /// - Empty <c>Any</c> list → NOT satisfied.
    /// </summary>
    public GroupEvaluationResult Evaluate(RuleConditionGroup? group, IFeatureContext context)
    {
        if (group is null)
            return new GroupEvaluationResult(false, new List<ConditionEvaluationResult>());

        var results = new List<ConditionEvaluationResult>();

        // ── AND branch ───────────────────────────────────────────────────────
        if (group.All is not null)
        {
            // Empty All list → not satisfied (safety default)
            if (!group.All.Any())
                return new GroupEvaluationResult(false, results);

            var satisfied = true;
            foreach (var cond in group.All)
            {
                var r = _conditionEvaluator.Evaluate(cond, context);
                results.Add(r);
                if (!r.Satisfied) satisfied = false;
            }
            return new GroupEvaluationResult(satisfied, results);
        }

        // ── OR branch ────────────────────────────────────────────────────────
        if (group.Any is not null)
        {
            if (!group.Any.Any())
                return new GroupEvaluationResult(false, results);

            var satisfied = false;
            foreach (var cond in group.Any)
            {
                var r = _conditionEvaluator.Evaluate(cond, context);
                results.Add(r);
                if (r.Satisfied) satisfied = true;
            }
            return new GroupEvaluationResult(satisfied, results);
        }

        // ── Nested groups ────────────────────────────────────────────────────
        if (group.Groups is { Count: > 0 })
        {
            // Default: AND across nested groups
            var satisfied = true;
            foreach (var subGroup in group.Groups)
            {
                var sub = Evaluate(subGroup, context);
                results.AddRange(sub.Results);
                if (!sub.Satisfied) satisfied = false;
            }
            return new GroupEvaluationResult(satisfied, results);
        }

        // No conditions and no groups → not satisfied
        return new GroupEvaluationResult(false, results);
    }
}
