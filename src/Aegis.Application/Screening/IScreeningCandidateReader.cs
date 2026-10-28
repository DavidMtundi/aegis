namespace Aegis.Application.Screening;

using Aegis.Modules.Screening.Domain;
using Aegis.Shared.Domain;

public interface IScreeningCandidateReader
{
    Task<ScreeningCandidate?> GetAsync(TenantId tenantId, Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>Customers ordered by id, after <paramref name="afterId"/>.</summary>
    Task<IReadOnlyList<ScreeningCandidate>> ListAsync(TenantId tenantId, Guid? afterId, int take, CancellationToken cancellationToken = default);
}
