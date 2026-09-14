namespace Aegis.Modules.Transactions.Domain;

/// <summary>
/// Represents the type of a transaction.
/// </summary>
public enum TransactionType
{
    EFT,
    WIRE,
    CASH_DEPOSIT,
    CASH_WITHDRAWAL,
    MOBILE,
    INTERNAL,
    DIRECT_DEBIT,
    STANDING_ORDER,
    REVERSAL,
    OTHER
}
