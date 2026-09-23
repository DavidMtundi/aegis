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
    Task<AmlRuleVersion?> GetActiveByTenantAndRuleIdAsync(TenantId tenantId, Guid ruleId, CancellationToken cancellationToken = default);
    Task<AmlRuleVersion?> GetByTenantAndIdAsync(TenantId tenantId, Guid versionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AmlRuleVersion>> ListByTenantAndRuleIdAsync(TenantId tenantId, Guid ruleId, CancellationToken cancellationToken = default);
    Task<int> GetMaxVersionNumberAsync(TenantId tenantId, Guid ruleId, CancellationToken cancellationToken = default);
    Task AddAsync(AmlRuleVersion version, CancellationToken cancellationToken = default);
}

public interface IStructuringRuleSeeder
{
    Task EnsureSeededAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}
