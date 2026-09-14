using System.Collections.Generic;
using Aegis.Modules.Aml.Domain;

namespace Aegis.Modules.Aml.Engine;

/// <summary>Result of evaluating exclusion conditions.</summary>
public record ExclusionResult(bool IsExcluded, string? ExclusionReason);

/// <summary>
/// Evaluates exclusion conditions from a rule definition.
/// Exclusions are represented as a flat <see cref="List{RuleCondition}"/> in the domain model.
/// If ANY exclusion condition matches, the evaluation is excluded (logical OR across exclusions).
/// </summary>
public sealed class ExclusionEvaluator
{
    private readonly ConditionEvaluator _conditionEvaluator;

    public ExclusionEvaluator(ConditionEvaluator conditionEvaluator)
    {
        _conditionEvaluator = conditionEvaluator;
    }

    /// <summary>
    /// Evaluates the list of exclusion conditions.
    /// Returns <c>IsExcluded = true</c> if ANY exclusion condition matches.
    /// </summary>
    public ExclusionResult Evaluate(List<RuleCondition>? exclusions, IFeatureContext context)
    {
        if (exclusions is null || exclusions.Count == 0)
            return new ExclusionResult(false, null);

        foreach (var exclusion in exclusions)
        {
            var result = _conditionEvaluator.Evaluate(exclusion, context);
            if (result.Satisfied)
            {
                return new ExclusionResult(true,
                    $"Excluded: {exclusion.Field} {exclusion.Operator} {exclusion.Value}");
            }
        }

        return new ExclusionResult(false, null);
    }
}
