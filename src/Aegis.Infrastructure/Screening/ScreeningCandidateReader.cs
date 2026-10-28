namespace Aegis.Infrastructure.Screening;

using Aegis.Application.Screening;
using Aegis.Infrastructure.Persistence;
using Aegis.Modules.Customers.Domain;
using Aegis.Modules.Screening.Domain;
using Aegis.Shared.Domain;
using Microsoft.EntityFrameworkCore;

public sealed class ScreeningCandidateReader : IScreeningCandidateReader
{
    private readonly AegisDbContext _db;

    public ScreeningCandidateReader(AegisDbContext db) => _db = db;

    public async Task<ScreeningCandidate?> GetAsync(TenantId tenantId, Guid customerId, CancellationToken cancellationToken = default)
        => (await Query(tenantId).Where(c => c.Id == customerId).Take(1).ToListAsync(cancellationToken))
            .Select(ToCandidate).FirstOrDefault();

    public async Task<IReadOnlyList<ScreeningCandidate>> ListAsync(TenantId tenantId, Guid? afterId, int take, CancellationToken cancellationToken = default)
    {
        var query = Query(tenantId);
        if (afterId is Guid after) query = query.Where(c => c.Id.CompareTo(after) > 0);
        var rows = await query.OrderBy(c => c.Id).Take(take).ToListAsync(cancellationToken);
        return rows.Select(ToCandidate).ToList();
    }

    private IQueryable<Customer> Query(TenantId tenantId) => _db.Customers.AsNoTracking().Where(c => c.TenantId == tenantId);

    private static ScreeningCandidate ToCandidate(Customer c) => c.Type == CustomerType.INDIVIDUAL
        ? new ScreeningCandidate(c.Id, $"{c.FirstName} {c.LastName}".Trim(), SubjectKind.INDIVIDUAL, c.DateOfBirth)
        : new ScreeningCandidate(c.Id, c.LegalName ?? "", SubjectKind.ENTITY, null);
}
