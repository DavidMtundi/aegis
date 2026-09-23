namespace Aegis.Modules.Transactions.Domain;

using System;
using System.Collections.Generic;
using Aegis.Shared.Domain;

/// <summary>
/// The canonical transaction model representing a financial movement.
/// </summary>
public sealed class CanonicalTransaction : AggregateRoot
{
    public TransactionId TransactionId { get; private set; }
    public string ExternalReference { get; private set; } = null!;
    public DateTimeOffset Timestamp { get; private set; }
    public DateTimeOffset ValueDate { get; private set; }
    public Money Amount { get; private set; } = null!;
    public TransactionDirection Direction { get; private set; }
    public TransactionType TransactionType { get; private set; }

    public AccountId AccountId { get; private set; }
    public CustomerId CustomerId { get; private set; }

    public AccountId? SourceAccountId { get; private set; }
    public AccountId? DestinationAccountId { get; private set; }

    public CustomerId? SourceCustomerId { get; private set; }
    public CustomerId? DestinationCustomerId { get; private set; }

    public string? SourceCountry { get; private set; }
    public string? DestinationCountry { get; private set; }
    public string? CounterpartyCountry { get; private set; }

    public TransactionChannel Channel { get; private set; }
    public string? ProductCode { get; private set; }
    public TransactionStatus Status { get; private set; }

    public string? CounterpartyId { get; private set; }
    public string? RawSourceId { get; private set; }

    public Dictionary<string, string> Metadata { get; private set; } = new();

    private CanonicalTransaction()
    {
    }

    public static CanonicalTransaction Ingest(
        TenantId tenantId,
        string externalReference,
        AccountId accountId,
        CustomerId customerId,
        DateTimeOffset timestamp,
        Money amount,
        TransactionDirection direction,
        TransactionType transactionType,
        TransactionChannel channel,
        string? counterpartyCountry = null,
        IDictionary<string, string>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(externalReference))
        {
            throw new ArgumentException("External reference is required.", nameof(externalReference));
        }

        if (!string.Equals(amount.Currency, "KES", StringComparison.Ordinal))
        {
            throw new ArgumentException("This slice only supports KES amounts.", nameof(amount));
        }

        var skew = TimeSpan.FromMinutes(5);
        if (timestamp > DateTimeOffset.UtcNow.Add(skew))
        {
            throw new ArgumentException("Transaction timestamp must not be materially in the future.", nameof(timestamp));
        }

        var id = Guid.NewGuid();
        var tx = new CanonicalTransaction
        {
            Id = id,
            TransactionId = new TransactionId(id),
            ExternalReference = externalReference.Trim(),
            TenantId = tenantId,
            Timestamp = timestamp,
            ValueDate = timestamp,
            Amount = amount,
            Direction = direction,
            TransactionType = transactionType,
            Channel = channel,
            Status = TransactionStatus.COMPLETED,
            AccountId = accountId,
            CustomerId = customerId,
            CounterpartyCountry = counterpartyCountry,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        if (direction is TransactionDirection.CREDIT or TransactionDirection.IN)
        {
            tx.DestinationAccountId = accountId;
            tx.DestinationCustomerId = customerId;
        }
        else
        {
            tx.SourceAccountId = accountId;
            tx.SourceCustomerId = customerId;
        }

        if (metadata is not null)
        {
            foreach (var pair in metadata)
            {
                tx.Metadata[pair.Key] = pair.Value;
            }
        }

        return tx;
    }
}
