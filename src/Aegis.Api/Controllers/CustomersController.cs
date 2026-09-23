namespace Aegis.Api.Controllers;

using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Modules.Customers.Application;
using Aegis.Modules.Customers.Domain;
using Aegis.Shared.Persistence;
using Aegis.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("api/v1/customers")]
public sealed class CustomersController : ControllerBase
{
    private readonly ICustomerRepository _customers;
    private readonly IAccountRepository _accounts;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public CustomersController(
        ICustomerRepository customers,
        IAccountRepository accounts,
        ITenantContext tenant,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _customers = customers;
        _accounts = accounts;
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

        return Created($"/api/v1/customers/{customer.Id}", new { id = customer.Id, type = customer.Type.ToString(), country = customer.Country });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var customer = await _customers.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        return customer is null ? NotFound() : Ok(ToCustomerResponse(customer));
    }

    [HttpGet]
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
        return Ok(new
        {
            items = result.Items.Select(ToCustomerResponse).ToList(),
            page = result.Page,
            pageSize = result.PageSize,
            totalCount = result.TotalCount
        });
    }

    [HttpPost("{id:guid}/accounts")]
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

    private static object ToCustomerResponse(Customer customer) => new
    {
        id = customer.Id,
        type = customer.Type.ToString(),
        country = customer.Country,
        firstName = customer.FirstName,
        lastName = customer.LastName,
        legalName = customer.LegalName,
        status = customer.Status.ToString(),
        externalReference = customer.ExternalReference
    };
}
