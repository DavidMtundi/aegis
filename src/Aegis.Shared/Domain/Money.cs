namespace Aegis.Shared.Domain;

using System;

/// <summary>
/// Value object representing an amount of money in a specific currency.
/// </summary>
public sealed record Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (amount < 0)
            throw new ArgumentException("Amount cannot be negative.", nameof(amount));
            
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3 || currency.ToUpperInvariant() != currency)
            throw new ArgumentException("Currency must be a 3-character uppercase ISO 4217 code.", nameof(currency));

        Amount = amount;
        Currency = currency;
    }

    public static Money Zero(string currency) => new(0m, currency);

    public Money Add(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException("Cannot add money with different currencies.");

        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException("Cannot subtract money with different currencies.");

        return new Money(Amount - other.Amount, Currency);
    }

    public static Money operator +(Money left, Money right) => left.Add(right);
    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public override string ToString() => $"{Amount} {Currency}";
}
