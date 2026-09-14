namespace Aegis.Modules.Identity.Domain;

/// <summary>
/// Represents the status of a tenant.
/// </summary>
public enum TenantStatus
{
    ACTIVE,
    SUSPENDED,
    ONBOARDING,
    TERMINATED
}
