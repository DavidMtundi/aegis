namespace Aegis.Api.Controllers;

using Aegis.Application.Transactions;
using Aegis.Modules.Transactions.Application;
using Aegis.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("api/v1/transactions")]
public sealed class TransactionsController : ControllerBase
{
    private readonly IIngestAndEvaluateStructuring _ingest;
    private readonly ITransactionRepository _transactions;
    private readonly ITenantContext _tenant;

    public TransactionsController(
        IIngestAndEvaluateStructuring ingest,
        ITransactionRepository transactions,
        ITenantContext tenant)
    {
        _ingest = ingest;
        _transactions = transactions;
        _tenant = tenant;
    }

    public sealed record IngestRequest(
        string ExternalReference,
        Guid AccountId,
        Guid CustomerId,
        decimal Amount,
        string Currency,
        string Direction,
        string TransactionType,
        string Channel,
        DateTimeOffset Timestamp,
        string? CounterpartyCountry,
        Dictionary<string, string>? Metadata);

    public sealed record EvaluationDto(
        Guid RuleId,
        Guid RuleVersionId,
        string RuleCode,
        int RuleVersion,
        bool IsTriggered,
        IReadOnlyDictionary<string, object> Features);

    public sealed record IngestResponse(
        Guid TransactionId,
        bool WasCreated,
        string ExternalReference,
        IReadOnlyList<EvaluationDto> Evaluations);

    [HttpPost]
    public async Task<ActionResult<IngestResponse>> Ingest([FromBody] IngestRequest request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();

        try
        {
            var result = await _ingest.ExecuteAsync(new IngestAndEvaluateStructuringCommand(
                _tenant.TenantId,
                _tenant.UserId,
                _tenant.Roles.FirstOrDefault(),
                new IngestTransactionPayload(
                    request.ExternalReference,
                    request.AccountId,
                    request.CustomerId,
                    request.Amount,
                    request.Currency,
                    request.Direction,
                    request.TransactionType,
                    request.Channel,
                    request.Timestamp,
                    request.CounterpartyCountry,
                    request.Metadata),
                HttpContext.TraceIdentifier), ct);

            var externalReference = request.ExternalReference.Trim();
            if (!result.WasCreated)
            {
                var existing = await _transactions.GetByTenantAndIdAsync(_tenant.TenantId, result.TransactionId, ct);
                externalReference = existing?.ExternalReference ?? externalReference;
            }

            var evaluations = result.Evaluations
                .Select(e => new EvaluationDto(
                    e.RuleId,
                    e.RuleVersionId,
                    e.RuleCode,
                    e.RuleVersion,
                    e.IsTriggered,
                    e.Features))
                .ToList();

            var response = new IngestResponse(result.TransactionId, result.WasCreated, externalReference, evaluations);
            return result.WasCreated
                ? Created($"/api/v1/transactions/{result.TransactionId}", response)
                : Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var tx = await _transactions.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        return tx is null ? NotFound() : Ok(new
        {
            id = tx.Id,
            externalReference = tx.ExternalReference,
            accountId = tx.AccountId.Value,
            customerId = tx.CustomerId.Value,
            amount = tx.Amount.Amount,
            currency = tx.Amount.Currency,
            direction = tx.Direction.ToString(),
            timestamp = tx.Timestamp,
            wasCreatedAt = tx.CreatedAt
        });
    }
}
