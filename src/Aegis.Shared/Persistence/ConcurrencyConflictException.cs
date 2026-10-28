namespace Aegis.Shared.Persistence;

/// <summary>
/// Raised when SaveChanges finds that a row changed since it was read (optimistic concurrency).
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(Exception innerException)
        : base("The record was changed by someone else.", innerException)
    {
    }
}
