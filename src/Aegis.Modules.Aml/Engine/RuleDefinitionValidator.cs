using System.Collections.Generic;
using System.Text.RegularExpressions;
using Aegis.Modules.Aml.Domain;

namespace Aegis.Modules.Aml.Engine;

/// <summary>Result of validating a rule definition.</summary>
public record ValidationResult(bool IsValid, List<string> Errors);

/// <summary>
/// Validates a <see cref="RuleDefinition"/> before it is saved as a rule version.
/// A definition must pass validation before it can enter the TESTING or PENDING_APPROVAL lifecycle state.
/// </summary>
public sealed class RuleDefinitionValidator
{
    private static readonly Regex ValidCode     = new("^[a-zA-Z0-9_]+$", RegexOptions.Compiled);
    private static readonly Regex ValidDuration = new(@"^\d+[dhm]$",     RegexOptions.Compiled);

    /// <summary>Validates a <see cref="RuleDefinition"/>.</summary>
    public ValidationResult Validate(RuleDefinition definition)
    {
        var errors = new List<string>();

        // ── Code ──────────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(definition.Code))
        {
            errors.Add("Code is required.");
        }
        else
        {
            if (definition.Code.Length > 100)
                errors.Add("Code must not exceed 100 characters.");

            if (!ValidCode.IsMatch(definition.Code))
                errors.Add("Code must contain only alphanumeric characters and underscores.");
        }

        // ── Name ──────────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(definition.Name))
            errors.Add("Name is required.");

        // ── Schedule ──────────────────────────────────────────────────────────
        if (definition.Schedule is not null)
        {
            if (!IsValidDuration(definition.Schedule.Frequency)
                && !string.Equals(definition.Schedule.Frequency, "realtime", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Schedule.Frequency '{definition.Schedule.Frequency}' is invalid. Expected format: '7d', '24h', '30m', or 'realtime'.");

            if (!IsValidDuration(definition.Schedule.Lookback))
                errors.Add($"Schedule.Lookback '{definition.Schedule.Lookback}' is invalid. Expected format: '14d', '24h', '30m'.");
        }

        // ── Conditions ────────────────────────────────────────────────────────
        if (definition.Conditions is null)
        {
            errors.Add("Conditions are required.");
        }
        else
        {
            ValidateConditionGroup(definition.Conditions, errors, "Conditions");
        }

        // ── RiskScore ─────────────────────────────────────────────────────────
        if (definition.RiskScore < 0 || definition.RiskScore > 100)
            errors.Add("RiskScore must be between 0 and 100 inclusive.");

        // ── Exclusions ────────────────────────────────────────────────────────
        if (definition.Exclusions is not null)
        {
            for (int i = 0; i < definition.Exclusions.Count; i++)
            {
                var excl = definition.Exclusions[i];
                if (!RuleOperator.IsValid(excl.Operator))
                    errors.Add($"Exclusion[{i}] has invalid operator: '{excl.Operator}'.");
                if (string.IsNullOrWhiteSpace(excl.Field))
                    errors.Add($"Exclusion[{i}] Field is required.");
            }
        }

        return new ValidationResult(errors.Count == 0, errors);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void ValidateConditionGroup(
        RuleConditionGroup group,
        List<string> errors,
        string path)
    {
        if (group.All is not null)
        {
            for (int i = 0; i < group.All.Count; i++)
                ValidateCondition(group.All[i], errors, $"{path}.All[{i}]");
        }

        if (group.Any is not null)
        {
            for (int i = 0; i < group.Any.Count; i++)
                ValidateCondition(group.Any[i], errors, $"{path}.Any[{i}]");
        }

        if (group.Groups is not null)
        {
            for (int i = 0; i < group.Groups.Count; i++)
                ValidateConditionGroup(group.Groups[i], errors, $"{path}.Groups[{i}]");
        }
    }

    private static void ValidateCondition(RuleCondition cond, List<string> errors, string path)
    {
        if (string.IsNullOrWhiteSpace(cond.Field))
            errors.Add($"{path}: Field is required.");

        if (!RuleOperator.IsValid(cond.Operator))
            errors.Add($"{path}: Invalid operator '{cond.Operator}'.");
    }

    private static bool IsValidDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration)) return false;
        return ValidDuration.IsMatch(duration);
    }
}
