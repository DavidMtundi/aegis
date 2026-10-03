namespace Aegis.Modules.Risk.Application;

using Aegis.Modules.Risk.Domain;
using Aegis.Shared.Domain;

public interface IRiskModelRepository
{
    Task<RiskModel?> GetActiveAsync(TenantId tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RiskModel>> ListVersionsAsync(TenantId tenantId, CancellationToken cancellationToken = default);
    Task AddAsync(RiskModel model, CancellationToken cancellationToken = default);
}

public interface ICustomerRiskScoreRepository
{
    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<CustomerRiskScore>> ListByCustomerAsync(TenantId tenantId, Guid customerId, int take, CancellationToken cancellationToken = default);
    Task AddAsync(CustomerRiskScore score, CancellationToken cancellationToken = default);
}
