namespace Aegis.Modules.Aml.Domain;

/// <summary>
/// Represents the scenario type of an AML rule.
/// </summary>
public enum ScenarioType
{
    HIGH_RISK_ENTITY,
    RAPID_MOVEMENT,
    HIGH_RISK_GEOGRAPHY,
    CASH_MONITORING,
    STRUCTURING,
    FUNDS_TRANSFER_PATTERN,
    NETWORK_ACTIVITY,
    INACTIVE_ACCOUNT_ESCALATION,
    EARLY_PAYOFF,
    CUSTOM,
    LARGE_VALUE,
    VELOCITY,
    ROUND_AMOUNT,
    NEW_ACCOUNT_ACTIVITY,
    MULTI_ACCOUNT,
    GEOGRAPHIC_ANOMALY,
    BEHAVIOUR_CHANGE
}
