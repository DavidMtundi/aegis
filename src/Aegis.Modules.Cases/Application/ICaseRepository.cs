namespace Aegis.Modules.Cases.Application;

using Aegis.Modules.Cases.Domain;
using Aegis.Shared.Domain;

public sealed record CaseListQuery(int Page = 1, int PageSize = 50);

public sealed record CaseListResult(IReadOnlyList<ComplianceCase> Items, int TotalCount, int Page, int PageSize);

public interface ICaseRepository
{
    Task<ComplianceCase?> GetByTenantAndIdAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<CaseListResult> ListByTenantAsync(TenantId tenantId, CaseListQuery query, CancellationToken cancellationToken = default);
    Task AddAsync(ComplianceCase complianceCase, CancellationToken cancellationToken = default);
}
