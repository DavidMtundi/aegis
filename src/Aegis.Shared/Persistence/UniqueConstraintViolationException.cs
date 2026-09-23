namespace Aegis.Shared.Persistence;

/// <summary>
/// Raised when SaveChanges hits a unique constraint (e.g. concurrent idempotent ingest).
/// </summary>
public sealed class UniqueConstraintViolationException : Exception
{
    public string? ConstraintName { get; }

    public UniqueConstraintViolationException(string? constraintName, Exception innerException)
        : base($"Unique constraint violated: {constraintName ?? "(unknown)"}", innerException)
    {
        ConstraintName = constraintName;
    }

    public bool IsTransactionExternalReference
        => ConstraintName is not null
           && ConstraintName.Contains("ExternalReference", StringComparison.OrdinalIgnoreCase);

    public bool IsAlertDeduplicationKey
        => ConstraintName is not null
           && ConstraintName.Contains("deduplication", StringComparison.OrdinalIgnoreCase);
}
