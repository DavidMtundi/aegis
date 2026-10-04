namespace Aegis.Modules.Aml.Application;

using System.Collections.Generic;
using Aegis.Modules.Aml.Domain;
using Aegis.Shared.Domain;

public sealed record DefaultRule(string Code, string Name, string Description, ScenarioType Scenario, RuleDefinition Definition);

/// <summary>
/// Starter scenarios every tenant receives. Thresholds are KES defaults for tenants to review and
/// tune through rule versions; they are not regulatory guidance.
/// </summary>
public static class DefaultRuleCatalog
{
    public const string Currency = "KES";

    public static IReadOnlyList<DefaultRule> All { get; } = new List<DefaultRule>
    {
        Rule("STRUCTURING_001", "Structuring",
            "Several transactions just under a reporting threshold within 24 hours.",
            ScenarioType.STRUCTURING, "24h", AlertSeverity.HIGH, 80,
            Gte("transaction_count_24h", 5), Gte("transaction_sum_24h", 450_000m), Lt("max_single_amount_24h", 100_000m)),

        Rule("RAPID_MOVEMENT_001", "Rapid movement / pass-through",
            "Funds entering and leaving within one hour.",
            ScenarioType.RAPID_MOVEMENT, "1h", AlertSeverity.HIGH, 75,
            Gte("credit_sum_1h", 50_000m), Gte("debit_sum_1h", 45_000m), Gte("pass_through_ratio_1h", 0.9m)),

        Rule("HIGH_RISK_GEOGRAPHY_001", "High-risk geography",
            "Transactions involving configured high-risk counterparty jurisdictions.",
            ScenarioType.HIGH_RISK_GEOGRAPHY, "24h", AlertSeverity.HIGH, 70,
            In("counterparty_country", "KP", "IR", "SY"), Gte("transaction_amount", 10_000m)),

        Rule("STRUCTURING_CASH_001", "Cash structuring",
            "Repeated cash deposits each kept under KES 100,000 within 24 hours.",
            ScenarioType.STRUCTURING, "24h", AlertSeverity.HIGH, 75,
            In("transaction_type", "CASH_DEPOSIT"), Gte("cash_deposit_count_24h", 3),
            Gte("cash_deposit_sum_24h", 250_000m), Lt("cash_deposit_max_24h", 100_000m)),

        Rule("STRUCTURING_7D_001", "Slow structuring over 7 days",
            "Many sub-threshold credits spread across a week to stay under daily monitoring.",
            ScenarioType.STRUCTURING, "7d", AlertSeverity.MEDIUM, 60,
            In("direction", "CREDIT"), Gte("transaction_count", 12), Gte("transaction_sum", 900_000m),
            Lt("max_single_amount", 100_000m)),

        Rule("LARGE_CASH_001", "Large cash transaction",
            "A single cash deposit or withdrawal of KES 1,000,000 or more.",
            ScenarioType.CASH_MONITORING, "24h", AlertSeverity.MEDIUM, 50,
            In("transaction_type", "CASH_DEPOSIT", "CASH_WITHDRAWAL"), Gte("transaction_amount", 1_000_000m)),

        Rule("LARGE_VALUE_001", "Large single transaction",
            "A single transaction of KES 5,000,000 or more on any channel.",
            ScenarioType.LARGE_VALUE, "24h", AlertSeverity.MEDIUM, 45,
            Gte("transaction_amount", 5_000_000m)),

        Rule("RAPID_MOVEMENT_24H_001", "Pass-through within 24 hours",
            "Large inflows mostly moved out again within a day.",
            ScenarioType.RAPID_MOVEMENT, "24h", AlertSeverity.HIGH, 70,
            Gte("credit_sum", 500_000m), Gte("debit_sum", 450_000m), Gte("pass_through_ratio", 0.9m)),

        Rule("VELOCITY_24H_001", "High transaction velocity",
            "An unusually high number of transactions within 24 hours.",
            ScenarioType.VELOCITY, "24h", AlertSeverity.MEDIUM, 50,
            Gte("transaction_count_24h", 20)),

        Rule("ROUND_AMOUNTS_001", "Round-amount activity",
            "Several exact multiples of KES 10,000 within 24 hours.",
            ScenarioType.ROUND_AMOUNT, "24h", AlertSeverity.MEDIUM, 45,
            Gte("round_amount_count_24h", 4), Gte("transaction_sum_24h", 200_000m)),

        Rule("DORMANT_REACTIVATION_001", "Dormant account reactivated",
            "A large transaction after 60 or more quiet days. Gaps longer than the 90-day window look like new customers and are left to other rules.",
            ScenarioType.INACTIVE_ACCOUNT_ESCALATION, "90d", AlertSeverity.HIGH, 65,
            Between("days_since_previous_transaction", 60m, 89.99m), Gte("transaction_amount", 100_000m)),

        Rule("DORMANT_SPIKE_001", "Dormant then sudden burst",
            "A burst of activity within 24 hours after 60 or more quiet days, typical of aged synthetic identities.",
            ScenarioType.INACTIVE_ACCOUNT_ESCALATION, "90d", AlertSeverity.HIGH, 75,
            Between("dormant_days_before_24h", 60m, 89.99m), Gte("transaction_count_24h", 3),
            Gte("transaction_sum_24h", 250_000m)),

        Rule("NEW_ACCOUNT_FLIGHT_001", "New customer moving large sums",
            "Large inflows moved straight out within 14 days of the first transaction.",
            ScenarioType.NEW_ACCOUNT_ACTIVITY, "30d", AlertSeverity.HIGH, 70,
            Lte("days_since_first_transaction", 14m), Gte("credit_sum", 500_000m), Gte("pass_through_ratio", 0.8m)),

        Rule("MULTI_ACCOUNT_001", "Activity across several accounts",
            "Significant value spread across three or more of the customer's accounts within 24 hours.",
            ScenarioType.MULTI_ACCOUNT, "24h", AlertSeverity.MEDIUM, 50,
            Gte("distinct_account_count_24h", 3), Gte("transaction_sum_24h", 300_000m)),

        Rule("GEO_SPREAD_7D_001", "Counterparties in many countries",
            "Counterparties in four or more countries within 7 days.",
            ScenarioType.GEOGRAPHIC_ANOMALY, "7d", AlertSeverity.MEDIUM, 50,
            Gte("distinct_counterparty_country_count", 4)),

        Rule("BEHAVIOUR_SPIKE_001", "Sudden change from 30-day baseline",
            "Value in the last 24 hours at least ten times the customer's daily average.",
            ScenarioType.BEHAVIOUR_CHANGE, "30d", AlertSeverity.MEDIUM, 55,
            Gte("daily_average_sum", 1_000m), Gte("sum_24h_vs_daily_average", 10m), Gte("transaction_sum_24h", 200_000m)),
    };

    private static DefaultRule Rule(
        string code, string name, string description, ScenarioType scenario, string lookback,
        AlertSeverity severity, int riskScore, params RuleCondition[] conditions)
    {
        var all = new List<RuleCondition> { In("currency", Currency) };
        all.AddRange(conditions);
        return new DefaultRule(code, name, description, scenario, new RuleDefinition
        {
            Code = code,
            Name = name,
            Focus = FocusType.CUSTOMER,
            Schedule = new RuleSchedule { Frequency = "realtime", Lookback = lookback },
            Conditions = new RuleConditionGroup { All = all },
            Severity = severity,
            RiskScore = riskScore,
            Actions = new List<string> { "CREATE_ALERT" }
        });
    }

    private static RuleCondition Gte(string field, decimal value) => new() { Field = field, Operator = ">=", Value = value };
    private static RuleCondition Lte(string field, decimal value) => new() { Field = field, Operator = "<=", Value = value };
    private static RuleCondition Lt(string field, decimal value) => new() { Field = field, Operator = "<", Value = value };
    private static RuleCondition In(string field, params string[] values) => new() { Field = field, Operator = "IN", Values = new List<object>(values) };
    private static RuleCondition Between(string field, decimal from, decimal to)
        => new() { Field = field, Operator = "BETWEEN", ValueFrom = from, ValueTo = to };
}
