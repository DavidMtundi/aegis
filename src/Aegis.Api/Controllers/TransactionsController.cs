namespace Aegis.Api.Controllers;

using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Modules.Customers.Application;
using Aegis.Modules.Transactions.Application;
using Aegis.Modules.Transactions.Domain;
using Aegis.Shared.Domain;
using Aegis.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("api/v1/transactions")]
public sealed class TransactionsController : ControllerBase
{
    private readonly ITransactionRepository _transactions;
    private readonly ICustomerRepository _customers;
    private readonly IAccountRepository _accounts;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;

    public TransactionsController(
        ITransactionRepository transactions,
        ICustomerRepository customers,
        IAccountRepository accounts,
        ITenantContext tenant,
        IAuditWriter audit)
    {
        _transactions = transactions;
        _customers = customers;
        _accounts = accounts;
        _tenant = tenant;
        _audit = audit;
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

    public sealed record IngestResponse(Guid TransactionId, bool WasCreated, string ExternalReference);

    [HttpPost]
    public async Task<ActionResult<IngestResponse>> Ingest([FromBody] IngestRequest request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();

        var existing = await _transactions.GetByTenantAndExternalReferenceAsync(_tenant.TenantId, request.ExternalReference, ct);
        if (existing is not null)
        {
            return Ok(new IngestResponse(existing.Id, false, existing.ExternalReference));
        }

        var customer = await _customers.GetByTenantAndIdAsync(_tenant.TenantId, request.CustomerId, ct);
        if (customer is null) return BadRequest("Customer not found in tenant.");

        var account = await _accounts.GetByTenantAndIdAsync(_tenant.TenantId, request.AccountId, ct);
        if (account is null) return BadRequest("Account not found in tenant.");
        if (account.CustomerId.Value != customer.Id) return BadRequest("Account does not belong to customer.");

        if (!Enum.TryParse<TransactionDirection>(request.Direction, true, out var direction))
            return BadRequest("Invalid Direction.");
        if (!Enum.TryParse<TransactionType>(request.TransactionType, true, out var type))
            return BadRequest("Invalid TransactionType.");
        if (!Enum.TryParse<TransactionChannel>(request.Channel, true, out var channel))
            return BadRequest("Invalid Channel.");

        try
        {
            var money = new Money(request.Amount, request.Currency.Trim().ToUpperInvariant());
            var tx = CanonicalTransaction.Ingest(
                _tenant.TenantId,
                request.ExternalReference,
                account.AccountId,
                customer.CustomerId,
                request.Timestamp,
                money,
                direction,
                type,
                channel,
                request.CounterpartyCountry,
                request.Metadata);

            await _transactions.AddAsync(tx, ct);
            await _audit.AppendAsync(AuditEvent.Create(
                _tenant.TenantId.Value,
                AuditEventTypes.TRANSACTION_INGESTED,
                nameof(CanonicalTransaction),
                tx.Id.ToString(),
                _tenant.UserId.ToString(),
                _tenant.Roles.FirstOrDefault(),
                null,
                null,
                "Transaction ingested",
                HttpContext.TraceIdentifier), ct);

            return Created($"/api/v1/transactions/{tx.Id}", new IngestResponse(tx.Id, true, tx.ExternalReference));
        }
        catch (ArgumentException ex)
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
