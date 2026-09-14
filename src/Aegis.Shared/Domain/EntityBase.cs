namespace Aegis.Shared.Domain;

using System;

/// <summary>
/// Base class for domain entities.
/// </summary>
public abstract class EntityBase : ITenantOwned
{
    public Guid Id { get; protected set; }
    public TenantId TenantId { get; protected set; }
    public DateTimeOffset CreatedAt { get; protected set; }
    public DateTimeOffset UpdatedAt { get; protected set; }
}
