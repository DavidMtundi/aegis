namespace Aegis.Api.Controllers;

using Aegis.Modules.Customers.Application;
using Aegis.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("api/v1/accounts")]
public sealed class AccountsController : ControllerBase
{
    private readonly IAccountRepository _accounts;
    private readonly ITenantContext _tenant;

    public AccountsController(IAccountRepository accounts, ITenantContext tenant)
    {
        _accounts = accounts;
        _tenant = tenant;
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated) return Unauthorized();
        var account = await _accounts.GetByTenantAndIdAsync(_tenant.TenantId, id, ct);
        return account is null ? NotFound() : Ok(new
        {
            id = account.Id,
            customerId = account.CustomerId.Value,
            accountType = account.AccountType.ToString(),
            currency = account.Currency,
            status = account.Status.ToString()
        });
    }
}
