namespace Aegis.Modules.Transactions.Domain;

/// <summary>
/// Represents the channel through which a transaction was made.
/// </summary>
public enum TransactionChannel
{
    MOBILE,
    BRANCH,
    ATM,
    ONLINE,
    API,
    BULK,
    INTERNAL
}
