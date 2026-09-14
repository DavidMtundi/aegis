namespace Aegis.Shared.Domain;

using System;

/// <summary>
/// Strongly-typed identifier for an account.
/// </summary>
public readonly record struct AccountId(Guid Value)
{
    public static AccountId New() => new(Guid.NewGuid());
    public static AccountId Empty => new(Guid.Empty);

    public static implicit operator Guid(AccountId id) => id.Value;
    public static explicit operator AccountId(Guid id) => new(id);
    
    public override string ToString() => Value.ToString();
}
