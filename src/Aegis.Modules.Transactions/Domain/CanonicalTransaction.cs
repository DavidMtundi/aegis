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
    public DateTimeOffset Timestamp { get; private set; }
    public DateTimeOffset ValueDate { get; private set; }
    public Money Amount { get; private set; } = null!;
    public TransactionDirection Direction { get; private set; }
    public TransactionType TransactionType { get; private set; }
    
    public AccountId? SourceAccountId { get; private set; }
    public AccountId? DestinationAccountId { get; private set; }
    
    public CustomerId? SourceCustomerId { get; private set; }
    public CustomerId? DestinationCustomerId { get; private set; }
    
    public string? SourceCountry { get; private set; }
    public string? DestinationCountry { get; private set; }
    
    public TransactionChannel Channel { get; private set; }
    public string? ProductCode { get; private set; }
    public TransactionStatus Status { get; private set; }
    
    public string? CounterpartyId { get; private set; }
    public string? RawSourceId { get; private set; }
    
    private readonly Dictionary<string, string> _metadata = new();
    public IReadOnlyDictionary<string, string> Metadata => _metadata;

    private CanonicalTransaction()
    {
    }

    public static CanonicalTransaction Create(
        TransactionId transactionId,
        TenantId tenantId,
        DateTimeOffset timestamp,
        DateTimeOffset valueDate,
        Money amount,
        TransactionDirection direction,
        TransactionType transactionType,
        TransactionChannel channel,
        TransactionStatus status)
    {
        var transaction = new CanonicalTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            TenantId = tenantId,
            Timestamp = timestamp,
            ValueDate = valueDate,
            Amount = amount,
            Direction = direction,
            TransactionType = transactionType,
            Channel = channel,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        return transaction;
    }
}
