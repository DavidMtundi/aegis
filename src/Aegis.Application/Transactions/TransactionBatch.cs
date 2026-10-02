namespace Aegis.Application.Transactions;

using Aegis.Shared.Domain;

public static class TransactionBatchStatus
{
    public const string Created = "CREATED";
    public const string Duplicate = "DUPLICATE";
    public const string Failed = "FAILED";
}

/// <param name="Row">1-based position in the request, or the CSV line number.</param>
public sealed record TransactionBatchItem(int Row, string? ExternalReference, IngestTransactionPayload? Payload, string? Error = null);

public sealed record TransactionBatchRowResult(
    int Row,
    string? ExternalReference,
    string Status,
    Guid? TransactionId,
    IReadOnlyList<Guid> AlertIds,
    string? Error);

public sealed record TransactionBatchResult(
    int Total,
    int Created,
    int Duplicates,
    int Failed,
    IReadOnlyList<TransactionBatchRowResult> Results);

public sealed record TransactionBatchCommand(
    TenantId TenantId,
    Guid ActorId,
    IReadOnlyCollection<string> ActorRoles,
    IReadOnlyList<TransactionBatchItem> Items,
    string CorrelationId);

public interface ITransactionBatchIngestor
{
    public const int MaxRows = 1000;

    /// <summary>
    /// Ingests rows in order, each in its own unit of work, so one bad row never rolls back the others.
    /// Items that already carry an Error are reported as failed without being ingested.
    /// </summary>
    Task<TransactionBatchResult> IngestAsync(TransactionBatchCommand command, CancellationToken cancellationToken = default);
}
