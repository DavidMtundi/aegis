namespace Aegis.Modules.Aml.Domain;

/// <summary>
/// Represents the status of an AML rule.
/// </summary>
public enum RuleStatus
{
    DRAFT,
    TESTING,
    PENDING_APPROVAL,
    APPROVED,
    SCHEDULED,
    ACTIVE,
    DISABLED,
    RETIRED
}
