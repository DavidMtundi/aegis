namespace Aegis.Modules.Aml.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aegis.Modules.Aml.Domain;
using Aegis.Shared.Domain;

public interface IAmlRuleRepository
{
    Task<AmlRule?> GetByTenantAndCodeAsync(TenantId tenantId, string code, CancellationToken cancellationToken = default);
    Task AddAsync(AmlRule rule, CancellationToken cancellationToken = default);
}

public interface IAmlRuleVersionRepository
{
    Task<IReadOnlyList<AmlRuleVersion>> GetActiveByTenantAsync(TenantId tenantId, CancellationToken cancellationToken = default);
    Task AddAsync(AmlRuleVersion version, CancellationToken cancellationToken = default);
}

public interface IStructuringRuleSeeder
{
    Task EnsureSeededAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}
