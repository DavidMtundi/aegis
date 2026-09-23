namespace Aegis.Modules.Transactions.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aegis.Modules.Transactions.Domain;
using Aegis.Shared.Domain;

public sealed record TransactionListQuery(
    Guid? CustomerId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 50);

public sealed record TransactionListResult(
    IReadOnlyList<CanonicalTransaction> Items,
    int TotalCount,
    int Page,
    int PageSize);

public interface ITransactionRepository
{
    Task<CanonicalTransaction?> GetByTenantAndIdAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<CanonicalTransaction?> GetByTenantAndExternalReferenceAsync(TenantId tenantId, string externalReference, CancellationToken cancellationToken = default);
    Task<TransactionListResult> ListByTenantAsync(TenantId tenantId, TransactionListQuery query, CancellationToken cancellationToken = default);
    Task AddAsync(CanonicalTransaction transaction, CancellationToken cancellationToken = default);
}

/// <summary>
/// Transactions-owned application contract. Implemented by Infrastructure.
/// </summary>
public interface ITransactionReadPort
{
    Task<IReadOnlyList<CanonicalTransaction>> GetForCustomerWindowAsync(
        TenantId tenantId,
        CustomerId customerId,
        DateTimeOffset windowStartInclusive,
        DateTimeOffset windowEndExclusive,
        CancellationToken cancellationToken = default);
}
