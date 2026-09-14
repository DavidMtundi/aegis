namespace Aegis.Modules.Transactions.Domain;

/// <summary>
/// Represents the quality status of a transaction during validation.
/// </summary>
public enum TransactionQualityStatus
{
    VALID,
    INVALID,
    QUARANTINED,
    REJECTED
}
