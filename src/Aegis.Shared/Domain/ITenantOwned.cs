namespace Aegis.Shared.Domain;

/// <summary>
/// Interface for entities that belong to a specific tenant.
/// </summary>
public interface ITenantOwned
{
    TenantId TenantId { get; }
}
