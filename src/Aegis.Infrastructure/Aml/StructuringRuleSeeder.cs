namespace Aegis.Infrastructure.Aml;

using Aegis.Modules.Aml.Application;
using Aegis.Modules.Aml.Domain;
using Aegis.Shared.Domain;

public sealed class StructuringRuleSeeder : IStructuringRuleSeeder
{
    public const string RuleCode = "STRUCTURING_001";
    public const string RapidMovementCode = "RAPID_MOVEMENT_001";

    private readonly IAmlRuleRepository _rules;
    private readonly IAmlRuleVersionRepository _versions;

    public StructuringRuleSeeder(
        IAmlRuleRepository rules,
        IAmlRuleVersionRepository versions)
    {
        _rules = rules;
        _versions = versions;
    }

    public async Task EnsureSeededAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        await EnsureStructuringAsync(tenantId, cancellationToken);
        await EnsureRapidMovementAsync(tenantId, cancellationToken);
    }

    private async Task EnsureStructuringAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var existing = await _rules.GetByTenantAndCodeAsync(tenantId, RuleCode, cancellationToken);
        if (existing is not null) return;

        var definition = new RuleDefinition
        {
            Code = RuleCode,
            Name = "Structuring",
            Focus = FocusType.CUSTOMER,
            Schedule = new RuleSchedule { Frequency = "realtime", Lookback = "24h" },
            Conditions = new RuleConditionGroup
            {
                All = new List<RuleCondition>
                {
                    new() { Field = "transaction_count_24h", Operator = ">=", Value = 5 },
                    new() { Field = "transaction_sum_24h", Operator = ">=", Value = 450000m },
                    new() { Field = "max_single_amount_24h", Operator = "<", Value = 100000m }
                }
            },
            Severity = AlertSeverity.HIGH,
            RiskScore = 80,
            Actions = new List<string> { "CREATE_ALERT" }
        };

        var rule = AmlRule.CreateDraft(
            tenantId, RuleCode, "Structuring",
            "Detects structuring via configurable thresholds.",
            ScenarioType.STRUCTURING, "system-seed");
        rule.Approve();
        rule.Activate();

        var version = AmlRuleVersion.CreateActive(
            rule.Id, tenantId, 1, definition, "system-seed", DateTimeOffset.UtcNow);

        await _rules.AddAsync(rule, cancellationToken);
        await _versions.AddAsync(version, cancellationToken);
    }

    private async Task EnsureRapidMovementAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var existing = await _rules.GetByTenantAndCodeAsync(tenantId, RapidMovementCode, cancellationToken);
        if (existing is not null) return;

        var definition = new RuleDefinition
        {
            Code = RapidMovementCode,
            Name = "Rapid movement / pass-through",
            Focus = FocusType.CUSTOMER,
            Schedule = new RuleSchedule { Frequency = "realtime", Lookback = "1h" },
            Conditions = new RuleConditionGroup
            {
                All = new List<RuleCondition>
                {
                    new() { Field = "credit_sum_1h", Operator = ">=", Value = 50_000m },
                    new() { Field = "debit_sum_1h", Operator = ">=", Value = 45_000m },
                    new() { Field = "pass_through_ratio_1h", Operator = ">=", Value = 0.9m }
                }
            },
            Severity = AlertSeverity.HIGH,
            RiskScore = 75,
            Actions = new List<string> { "CREATE_ALERT" }
        };

        var rule = AmlRule.CreateDraft(
            tenantId, RapidMovementCode, "Rapid movement / pass-through",
            "Detects funds entering and leaving within one hour.",
            ScenarioType.RAPID_MOVEMENT, "system-seed");
        rule.Approve();
        rule.Activate();

        var version = AmlRuleVersion.CreateActive(
            rule.Id, tenantId, 1, definition, "system-seed", DateTimeOffset.UtcNow);

        await _rules.AddAsync(rule, cancellationToken);
        await _versions.AddAsync(version, cancellationToken);
    }
}
