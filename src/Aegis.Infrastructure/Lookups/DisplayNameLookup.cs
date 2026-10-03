namespace Aegis.Infrastructure.Lookups;

using Aegis.Application.Lookups;
using Aegis.Infrastructure.Persistence;
using Aegis.Shared.Domain;
using Microsoft.EntityFrameworkCore;

public sealed class DisplayNameLookup : IDisplayNameLookup
{
    private readonly AegisDbContext _db;

    public DisplayNameLookup(AegisDbContext db) => _db = db;

    public async Task<IReadOnlyDictionary<Guid, CustomerLabel>> CustomersAsync(
        TenantId tenantId, IEnumerable<Guid> customerIds, CancellationToken cancellationToken = default)
    {
        var ids = customerIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, CustomerLabel>();

        var rows = await _db.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && ids.Contains(c.Id))
            .Select(c => new { c.Id, c.LegalName, c.FirstName, c.LastName, c.Country, c.Type })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            r => r.Id,
            r => new CustomerLabel(
                r.Id,
                !string.IsNullOrWhiteSpace(r.LegalName) ? r.LegalName! : $"{r.FirstName} {r.LastName}".Trim(),
                r.Country,
                r.Type.ToString()));
    }

    public async Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(
        TenantId tenantId, IEnumerable<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();

        return await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);
    }
}
