namespace Aegis.Modules.Customers.Application;

using System;
using System.Threading;
using System.Threading.Tasks;
using Aegis.Modules.Customers.Domain;
using Aegis.Shared.Domain;

public sealed record CustomerListQuery(string? Q = null, int Page = 1, int PageSize = 50);

public sealed record CustomerListResult(IReadOnlyList<Customer> Items, int TotalCount, int Page, int PageSize);

public interface ICustomerRepository
{
    Task<Customer?> GetByTenantAndIdAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<CustomerListResult> ListByTenantAsync(TenantId tenantId, CustomerListQuery query, CancellationToken cancellationToken = default);
    Task AddAsync(Customer customer, CancellationToken cancellationToken = default);
}

public interface IAccountRepository
{
    Task<Account?> GetByTenantAndIdAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Account account, CancellationToken cancellationToken = default);
}
