namespace Aegis.Api.Controllers;

using Aegis.Api.Authorization;
using Aegis.Application.Lookups;
using Aegis.Application.Risk;
using Aegis.Modules.Alerts.Application;
using Aegis.Modules.Alerts.Domain;
using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Modules.Cases.Application;
using Aegis.Modules.Cases.Domain;
using Aegis.Modules.Customers.Application;
using Aegis.Modules.Customers.Domain;
using Aegis.Modules.Risk.Domain;
using Aegis.Modules.Transactions.Application;
using Aegis.Shared.Domain;
using Aegis.Shared.Persistence;
using Aegis.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("api/v1/customers")]
public sealed class CustomersController : ControllerBase
{
    /// <summary>Most recent items per section in the customer overview.</summary>
    private const int OverviewTake = 50;

    private readonly ICustomerRepository _customers;
    private readonly IAccountRepository _accounts;
    private readonly ITransactionRepository _transactions;
    private readonly IAlertRepository _alerts;
    private readonly ICaseRepository _cases;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly ICustomerRiskService _risk;
    private readonly IDisplayNameLookup _names;

    public CustomersController(
        ICustomerRepository customers,
        IAccountRepository accounts,
        ITransactionRepository transactions,
        IAlertRepository alerts,
        ICaseRepository cases,
        ITenantContext tenant,
        IAuditWriter audit,
        IUnitOfWork uow,
        ICustomerRiskService risk,
        IDisplayNameLookup names)
    {
        _names = names;
        _risk = risk;
        _customers = customers;
        _accounts = accounts;
        _transactions = transactions;
        _alerts = alerts;
        _cases = cases;
        _tenant = tenant;
        _audit = audit;
        _uow = uow;
    }

    public sealed record CreateCustomerRequest(
        string Type,
        string Country,
        string? ExternalReference,
        string? FirstName,
        string? LastName,
        string? LegalName);

    public sealed record CreateAccountRequest(
        string AccountType,
        string Currency,
        string? ExternalReference);

    [HttpPost]
    [RequirePermission(Permissions.CustomerWrite)]
    public async Task<IActionResult> Create([FromBody] CreateCustomerRequest request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();

        Customer customer;
        if (string.Equals(request.Type, "INDIVIDUAL", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
                return BadRequest("FirstName and LastName are required for individuals.");
            customer = Customer.CreateIndividual(_tenant.TenantId, request.ExternalReference, request.Country, request.FirstName, request.LastName);
        }
        else if (string.Equals(request.Type, "BUSINESS", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.LegalName))
                return BadRequest("LegalName is required for businesses.");
            customer = Customer.CreateBusiness(_tenant.TenantId, request.ExternalReference, request.Country, request.LegalName);
        }
        else
        {
            return BadRequest("Type must be INDIVIDUAL or BUSINESS.");
        }

        await _customers.AddAsync(customer, ct);
        await _audit.AppendAsync(AuditEvent.Create(
            _tenant.TenantId.Value,
            AuditEventTypes.CUSTOMER_CREATED,
            nameof(Customer),
            customer.Id.ToString(),
            _tenant.UserId.ToString(),
            _tenant.Roles.FirstOrDefault(),
            null,
            null,
            "Customer created",
            HttpContext.TraceIdentifier), ct);
        await _uow.SaveChangesAsync(ct);
        await _risk.TryRecalculateAsync(
            _tenant.TenantId, customer.Id, RiskTriggers.CustomerCreated,
            new RiskActor(_tenant.UserId.ToString(), _tenant.Roles.FirstOrDefault(), HttpContext.TraceIdentifier), ct);

        return Created($"/api/v1/customers/{customer.Id}", new { id = customer.Id, type = customer.Type.ToString(), country = customer.Country });
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.CustomerRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var customer = await _customers.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (customer is null) return NotFound();
        var bands = await _names.RiskBandsAsync(_tenant.TenantId, new[] { customer.Id }, ct);
        return Ok(ToCustomerResponse(customer, bands.GetValueOrDefault(customer.Id)));
    }

    [HttpGet("{id:guid}/overview")]
    [RequirePermission(Permissions.CustomerRead)]
    public async Task<IActionResult> Overview(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var customer = await _customers.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (customer is null) return NotFound();

        var accounts = await _accounts.ListByCustomerAsync(_tenant.TenantId, customer.CustomerId, ct);

        TransactionListResult? transactions = null;
        if (Can(Permissions.TransactionRead))
            transactions = await _transactions.ListByTenantAsync(
                _tenant.TenantId, new TransactionListQuery(CustomerId: id, PageSize: OverviewTake), ct);

        IReadOnlyList<Alert>? alerts = null;
        if (Can(Permissions.AlertRead))
            alerts = await _alerts.ListByFocusAsync(_tenant.TenantId, FocusType.CUSTOMER, id.ToString(), OverviewTake, ct);

        IReadOnlyList<ComplianceCase>? cases = null;
        if (Can(Permissions.CaseRead))
            cases = await _cases.ListByCustomerAsync(_tenant.TenantId, id, OverviewTake, ct);

        var bands = await _names.RiskBandsAsync(_tenant.TenantId, new[] { customer.Id }, ct);
        var customerName = (await _names.CustomersAsync(_tenant.TenantId, new[] { customer.Id }, ct)).GetValueOrDefault(customer.Id)?.Name;
        var caseResponses = cases is null ? null : await CasesController.ToResponsesAsync(_names, _tenant.TenantId, cases, ct);

        return Ok(new
        {
            customer = ToCustomerResponse(customer, bands.GetValueOrDefault(customer.Id)),
            accounts = accounts.Select(a => new
            {
                id = a.Id,
                accountType = a.AccountType.ToString(),
                currency = a.Currency,
                status = a.Status.ToString(),
                externalReference = a.ExternalReference,
                openedAt = a.OpenedAt
            }).ToList(),
            recentTransactions = transactions?.Items.Select(t => TransactionsController.ToTransactionResponse(t, customerName)).ToList(),
            alerts = alerts?.Select(a => new
            {
                id = a.Id,
                ruleName = a.Evidence.RuleName,
                severity = a.Severity.ToString(),
                status = a.Status.ToString(),
                riskScore = a.RiskScore,
                triggeredAt = a.TriggeredAt
            }).ToList(),
            cases = caseResponses,
            summary = new
            {
                transactionCount = transactions?.TotalCount,
                openAlerts = alerts?.Count(a => !a.IsClosed),
                openCases = cases?.Count(c => c.Status != CaseStatus.CLOSED)
            }
        });
    }

    private bool Can(string permission) => RolePermissions.Grants(_tenant.Roles, permission);

    [HttpGet]
    [RequirePermission(Permissions.CustomerRead)]
    public async Task<IActionResult> List(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var result = await _customers.ListByTenantAsync(
            _tenant.TenantId,
            new CustomerListQuery(q, page, pageSize),
            ct);
        var bands = await _names.RiskBandsAsync(_tenant.TenantId, result.Items.Select(c => c.Id), ct);
        return Ok(new
        {
            items = result.Items.Select(c => ToCustomerResponse(c, bands.GetValueOrDefault(c.Id))).ToList(),
            page = result.Page,
            pageSize = result.PageSize,
            totalCount = result.TotalCount
        });
    }

    [HttpPost("{id:guid}/accounts")]
    [RequirePermission(Permissions.CustomerWrite)]
    public async Task<IActionResult> CreateAccount(Guid id, [FromBody] CreateAccountRequest request, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var customer = await _customers.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        if (customer is null) return NotFound();

        if (!Enum.TryParse<AccountType>(request.AccountType, true, out var accountType))
            return BadRequest("Invalid AccountType.");

        try
        {
            var account = Account.Create(_tenant.TenantId, customer.CustomerId, request.ExternalReference, accountType, request.Currency);
            await _accounts.AddAsync(account, ct);
            await _audit.AppendAsync(AuditEvent.Create(
                _tenant.TenantId.Value,
                AuditEventTypes.ACCOUNT_CREATED,
                nameof(Account),
                account.Id.ToString(),
                _tenant.UserId.ToString(),
                _tenant.Roles.FirstOrDefault(),
                null,
                null,
                "Account created",
                HttpContext.TraceIdentifier), ct);
            await _uow.SaveChangesAsync(ct);

            return Created($"/api/v1/accounts/{account.Id}", new
            {
                id = account.Id,
                customerId = account.CustomerId.Value,
                accountType = account.AccountType.ToString(),
                currency = account.Currency
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private static object ToCustomerResponse(Customer customer, string? riskBand) => new
    {
        id = customer.Id,
        type = customer.Type.ToString(),
        country = customer.Country,
        firstName = customer.FirstName,
        lastName = customer.LastName,
        legalName = customer.LegalName,
        status = customer.Status.ToString(),
        externalReference = customer.ExternalReference,
        riskBand
    };
}
