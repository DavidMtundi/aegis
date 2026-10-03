namespace Aegis.Infrastructure.Transactions;

using Aegis.Application.Transactions;
using Aegis.Shared.Security;
using Microsoft.Extensions.DependencyInjection;

public sealed class TransactionBatchIngestor : ITransactionBatchIngestor
{
    private readonly IServiceScopeFactory _scopes;

    public TransactionBatchIngestor(IServiceScopeFactory scopes) => _scopes = scopes;

    public async Task<TransactionBatchResult> IngestAsync(TransactionBatchCommand command, CancellationToken cancellationToken = default)
    {
        var results = new List<TransactionBatchRowResult>(command.Items.Count);

        foreach (var item in command.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Payload is null || item.Error is not null)
            {
                results.Add(Failed(item, item.Error ?? "Row is empty."));
                continue;
            }

            results.Add(await IngestRowAsync(command, item, item.Payload, cancellationToken));
        }

        return new TransactionBatchResult(
            results.Count,
            results.Count(r => r.Status == TransactionBatchStatus.Created),
            results.Count(r => r.Status == TransactionBatchStatus.Duplicate),
            results.Count(r => r.Status == TransactionBatchStatus.Failed),
            results);
    }

    private async Task<TransactionBatchRowResult> IngestRowAsync(
        TransactionBatchCommand command, TransactionBatchItem item, IngestTransactionPayload payload, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenant.TenantId = command.TenantId;
        tenant.UserId = command.ActorId;
        tenant.Roles = command.ActorRoles;
        tenant.IsAuthenticated = true;

        var ingest = scope.ServiceProvider.GetRequiredService<IIngestAndEvaluateRules>();
        try
        {
            var result = await ingest.ExecuteAsync(new IngestAndEvaluateRulesCommand(
                command.TenantId,
                command.ActorId,
                command.ActorRoles.FirstOrDefault(),
                payload,
                command.CorrelationId), ct);

            return new TransactionBatchRowResult(
                item.Row,
                payload.ExternalReference.Trim(),
                result.WasCreated ? TransactionBatchStatus.Created : TransactionBatchStatus.Duplicate,
                result.TransactionId,
                result.AlertIds,
                null);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Failed(item, ex.Message);
        }
    }

    private static TransactionBatchRowResult Failed(TransactionBatchItem item, string error)
        => new(item.Row, item.ExternalReference, TransactionBatchStatus.Failed, null, Array.Empty<Guid>(), error);
}
