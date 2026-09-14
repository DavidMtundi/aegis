namespace Aegis.Shared.Domain;

using System;

/// <summary>
/// Strongly-typed identifier for a tenant.
/// </summary>
public readonly record struct TenantId(Guid Value)
{
    public static TenantId New() => new(Guid.NewGuid());
    public static TenantId Empty => new(Guid.Empty);

    public static implicit operator Guid(TenantId id) => id.Value;
    public static explicit operator TenantId(Guid id) => new(id);
    
    public override string ToString() => Value.ToString();
}
