namespace Aegis.Shared.Domain;

using System;

/// <summary>
/// Strongly-typed identifier for a customer.
/// </summary>
public readonly record struct CustomerId(Guid Value)
{
    public static CustomerId New() => new(Guid.NewGuid());
    public static CustomerId Empty => new(Guid.Empty);

    public static implicit operator Guid(CustomerId id) => id.Value;
    public static explicit operator CustomerId(Guid id) => new(id);
    
    public override string ToString() => Value.ToString();
}
