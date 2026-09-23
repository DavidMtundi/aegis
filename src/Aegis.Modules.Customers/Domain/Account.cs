namespace Aegis.Modules.Customers.Domain;

using System;
using Aegis.Shared.Domain;

public sealed class Account : AggregateRoot
{
    public AccountId AccountId { get; private set; }
    public CustomerId CustomerId { get; private set; }
    public string? ExternalReference { get; private set; }
    public AccountType AccountType { get; private set; }
    public string Currency { get; private set; } = null!;
    public AccountStatus Status { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }

    private Account() { }

    public static Account Create(
        TenantId tenantId,
        CustomerId customerId,
        string? externalReference,
        AccountType accountType,
        string currency)
    {
        if (!string.Equals(currency, "KES", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("This slice only supports KES accounts.", nameof(currency));
        }

        var id = Guid.NewGuid();
        return new Account
        {
            Id = id,
            AccountId = new AccountId(id),
            TenantId = tenantId,
            CustomerId = customerId,
            ExternalReference = externalReference,
            AccountType = accountType,
            Currency = "KES",
            Status = AccountStatus.ACTIVE,
            OpenedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
