namespace Aegis.Modules.Aml.Domain;

/// <summary>
/// Represents the status of an AML rule version.
/// </summary>
public enum RuleVersionStatus
{
    DRAFT,
    TESTING,
    PENDING_APPROVAL,
    APPROVED,
    ACTIVE,
    SUPERSEDED,
    RETIRED
}
