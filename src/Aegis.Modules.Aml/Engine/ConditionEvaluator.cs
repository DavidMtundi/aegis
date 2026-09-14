using System;
using System.Collections.Generic;
using System.Linq;
using Aegis.Modules.Aml.Domain;

namespace Aegis.Modules.Aml.Engine;

/// <summary>Result of evaluating a single rule condition.</summary>
public record ConditionEvaluationResult(
    bool Satisfied,
    string ConditionDescription,
    object? ActualValue,
    string Operator,
    object? ExpectedValue);

/// <summary>
/// Evaluates a single <see cref="RuleCondition"/> against an <see cref="IFeatureContext"/>.
/// Uses <c>Field</c> as the feature lookup key, matching the domain model.
/// </summary>
public sealed class ConditionEvaluator
{
    /// <summary>
    /// Evaluates one condition. Returns a <see cref="ConditionEvaluationResult"/>
    /// that explains what was measured, what the threshold was, and whether it was satisfied.
    /// </summary>
    public ConditionEvaluationResult Evaluate(RuleCondition condition, IFeatureContext context)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var featureName  = condition.Field;
        var op           = (condition.Operator ?? "=").ToUpperInvariant();
        var hasFeature   = context.HasFeature(featureName);
        object? actual   = hasFeature ? context.GetAll()[featureName] : null;
        object? expected = condition.Value;
        bool satisfied;

        switch (op)
        {
            case RuleOperator.IsNull:
                satisfied = !hasFeature || actual is null;
                expected  = "NULL";
                break;

            case RuleOperator.IsNotNull:
                satisfied = hasFeature && actual is not null;
                expected  = "NOT NULL";
                break;

            case RuleOperator.Equal:
                satisfied = AreEqual(actual, condition.Value);
                break;

            case RuleOperator.NotEqual:
                satisfied = !AreEqual(actual, condition.Value);
                break;

            case RuleOperator.GreaterThan:
                satisfied = Compare(actual, condition.Value) > 0;
                break;

            case RuleOperator.GreaterThanOrEqual:
                satisfied = Compare(actual, condition.Value) >= 0;
                break;

            case RuleOperator.LessThan:
                satisfied = Compare(actual, condition.Value) < 0;
                break;

            case RuleOperator.LessThanOrEqual:
                satisfied = Compare(actual, condition.Value) <= 0;
                break;

            case RuleOperator.Between:
                satisfied = Compare(actual, condition.ValueFrom) >= 0
                         && Compare(actual, condition.ValueTo)   <= 0;
                expected = $"{condition.ValueFrom} AND {condition.ValueTo}";
                break;

            case RuleOperator.In:
                satisfied = condition.Values is not null
                         && condition.Values.Any(v => AreEqual(actual, v));
                expected = condition.Values is not null
                    ? string.Join(", ", condition.Values)
                    : string.Empty;
                break;

            case RuleOperator.NotIn:
                satisfied = condition.Values is null
                         || !condition.Values.Any(v => AreEqual(actual, v));
                expected = condition.Values is not null
                    ? string.Join(", ", condition.Values)
                    : string.Empty;
                break;

            case RuleOperator.Contains:
                satisfied = actual?.ToString()
                    ?.Contains(condition.Value?.ToString() ?? string.Empty,
                               StringComparison.OrdinalIgnoreCase) == true;
                break;

            case RuleOperator.StartsWith:
                satisfied = actual?.ToString()
                    ?.StartsWith(condition.Value?.ToString() ?? string.Empty,
                                 StringComparison.OrdinalIgnoreCase) == true;
                break;

            default:
                throw new InvalidOperationException($"Unsupported operator: '{op}'");
        }

        var description = $"{featureName} {op} {expected}";
        return new ConditionEvaluationResult(satisfied, description, actual, op, expected);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool AreEqual(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a.Equals(b)) return true;

        if (a is bool boolA && b is not bool)
        {
            // JsonElement serializes booleans; handle string "true"/"false"
            if (bool.TryParse(b.ToString(), out var parsedB))
                return boolA == parsedB;
        }
        if (b is bool boolB && a is not bool)
        {
            if (bool.TryParse(a.ToString(), out var parsedA))
                return parsedA == boolB;
        }

        if (a is IConvertible && b is IConvertible)
        {
            try { return Convert.ToDecimal(a) == Convert.ToDecimal(b); }
            catch { return string.Equals(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase); }
        }

        return string.Equals(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static int Compare(object? a, object? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return  1;

        if (a is IComparable compA && a.GetType() == b.GetType())
            return compA.CompareTo(b);

        try { return Convert.ToDecimal(a).CompareTo(Convert.ToDecimal(b)); }
        catch { return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal); }
    }
}
