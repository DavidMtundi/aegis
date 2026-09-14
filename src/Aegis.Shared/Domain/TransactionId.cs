namespace Aegis.Shared.Domain;

using System;

/// <summary>
/// Strongly-typed identifier for a transaction.
/// </summary>
public readonly record struct TransactionId(Guid Value)
{
    public static TransactionId New() => new(Guid.NewGuid());
    public static TransactionId Empty => new(Guid.Empty);

    public static implicit operator Guid(TransactionId id) => id.Value;
    public static explicit operator TransactionId(Guid id) => new(id);
    
    public override string ToString() => Value.ToString();
}
