namespace Aegis.Modules.Transactions.Domain;

/// <summary>
/// Represents the status of a transaction.
/// </summary>
public enum TransactionStatus
{
    PENDING,
    COMPLETED,
    REVERSED,
    CANCELLED,
    FAILED
}
