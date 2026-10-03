namespace Aegis.Api.Controllers;

using Aegis.Api.Authorization;
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
    private readonly IIngestAndEvaluateRules _ingest;
    private readonly ITransactionBatchIngestor _batch;
    private readonly ITransactionRepository _transactions;
    private readonly ITenantContext _tenant;

    public TransactionsController(
        IIngestAndEvaluateRules ingest,
        ITransactionBatchIngestor batch,
        ITransactionRepository transactions,
        ITenantContext tenant)
    {
        _ingest = ingest;
        _batch = batch;
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
        IReadOnlyList<EvaluationDto> Evaluations,
        IReadOnlyList<Guid> AlertIds);

    [HttpPost]
    [RequirePermission(Permissions.TransactionWrite)]
    public async Task<ActionResult<IngestResponse>> Ingest([FromBody] IngestRequest request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();

        try
        {
            var result = await _ingest.ExecuteAsync(new IngestAndEvaluateRulesCommand(
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

            var response = new IngestResponse(
                result.TransactionId,
                result.WasCreated,
                externalReference,
                evaluations,
                result.AlertIds);
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

    public sealed record BatchIngestRequest(IReadOnlyList<IngestRequest>? Transactions);

    private const long MaxImportBytes = 5 * 1024 * 1024;

    [HttpPost("batch")]
    [RequirePermission(Permissions.TransactionWrite)]
    public async Task<ActionResult<TransactionBatchResult>> Batch([FromBody] BatchIngestRequest request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var rows = request.Transactions ?? Array.Empty<IngestRequest>();
        if (rows.Count == 0) return BadRequest("At least one transaction is required.");
        if (rows.Count > ITransactionBatchIngestor.MaxRows)
            return BadRequest($"A batch can contain at most {ITransactionBatchIngestor.MaxRows} transactions.");

        var items = rows.Select((r, i) => r is null
                ? new TransactionBatchItem(i + 1, null, null, "Row is empty.")
                : new TransactionBatchItem(i + 1, r.ExternalReference?.Trim(), ToPayload(r)))
            .ToList();
        return Ok(await _batch.IngestAsync(BatchCommand(items), ct));
    }

    [HttpPost("import")]
    [RequirePermission(Permissions.TransactionWrite)]
    [RequestSizeLimit(MaxImportBytes)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<TransactionBatchResult>> Import(IFormFile? file, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        if (file is null || file.Length == 0) return BadRequest("Upload a CSV file in the 'file' form field.");

        string csv;
        using (var reader = new StreamReader(file.OpenReadStream()))
            csv = await reader.ReadToEndAsync(ct);

        var parsed = TransactionCsvParser.Parse(csv);
        if (parsed.HeaderErrors.Count > 0) return BadRequest(string.Join(" ", parsed.HeaderErrors));
        if (parsed.Rows.Count == 0) return BadRequest("The CSV has no data rows.");
        if (parsed.Rows.Count > ITransactionBatchIngestor.MaxRows)
            return BadRequest($"An import can contain at most {ITransactionBatchIngestor.MaxRows} rows.");

        var items = parsed.Rows
            .Select(r => new TransactionBatchItem(r.LineNumber, r.ExternalReference, r.Payload, r.Error))
            .ToList();
        return Ok(await _batch.IngestAsync(BatchCommand(items), ct));
    }

    private TransactionBatchCommand BatchCommand(IReadOnlyList<TransactionBatchItem> items)
        => new(_tenant.TenantId, _tenant.UserId, _tenant.Roles, items, HttpContext.TraceIdentifier);

    private static IngestTransactionPayload ToPayload(IngestRequest r) => new(
        r.ExternalReference ?? string.Empty,
        r.AccountId,
        r.CustomerId,
        r.Amount,
        r.Currency ?? string.Empty,
        r.Direction ?? string.Empty,
        r.TransactionType ?? string.Empty,
        r.Channel ?? string.Empty,
        r.Timestamp,
        r.CounterpartyCountry,
        r.Metadata);

    [HttpGet]
    [RequirePermission(Permissions.TransactionRead)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? customerId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var result = await _transactions.ListByTenantAsync(
            _tenant.TenantId,
            new TransactionListQuery(customerId, from, to, page, pageSize),
            ct);
        return Ok(new
        {
            items = result.Items.Select(ToTransactionResponse).ToList(),
            page = result.Page,
            pageSize = result.PageSize,
            totalCount = result.TotalCount
        });
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.TransactionRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var tx = await _transactions.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        return tx is null ? NotFound() : Ok(ToTransactionResponse(tx));
    }

    internal static object ToTransactionResponse(Modules.Transactions.Domain.CanonicalTransaction tx) => new
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
    };
}
