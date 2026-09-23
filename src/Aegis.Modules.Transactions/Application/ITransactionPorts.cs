namespace Aegis.Modules.Transactions.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aegis.Modules.Transactions.Domain;
using Aegis.Shared.Domain;

public interface ITransactionRepository
{
    Task<CanonicalTransaction?> GetByTenantAndIdAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<CanonicalTransaction?> GetByTenantAndExternalReferenceAsync(TenantId tenantId, string externalReference, CancellationToken cancellationToken = default);
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
