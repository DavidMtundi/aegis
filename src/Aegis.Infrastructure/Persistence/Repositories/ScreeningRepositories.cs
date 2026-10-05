namespace Aegis.Infrastructure.Persistence.Repositories;

using Aegis.Modules.Screening.Application;
using Aegis.Modules.Screening.Domain;
using Aegis.Shared.Contracts;
using Aegis.Shared.Domain;
using Microsoft.EntityFrameworkCore;

public sealed class WatchlistRepository : IWatchlistRepository
{
    private readonly AegisDbContext _db;

    public WatchlistRepository(AegisDbContext db) => _db = db;

    public async Task<IReadOnlyList<WatchlistLoad>> ListLatestLoadsAsync(CancellationToken cancellationToken = default)
    {
        var loads = await _db.WatchlistLoads.AsNoTracking().ToListAsync(cancellationToken);
        return loads.GroupBy(l => l.Source).Select(g => g.MaxBy(l => l.LoadedAt)!).OrderBy(l => l.Source).ToList();
    }

    public Task<WatchlistLoad?> GetLatestLoadAsync(WatchlistSource source, CancellationToken cancellationToken = default)
        => _db.WatchlistLoads.AsNoTracking()
            .Where(l => l.Source == source)
            .OrderByDescending(l => l.LoadedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<DateTimeOffset?> GetLastLoadedAtAsync(CancellationToken cancellationToken = default)
        => await _db.WatchlistLoads.AsNoTracking().MaxAsync(l => (DateTimeOffset?)l.LoadedAt, cancellationToken);

    public async Task<IReadOnlyList<WatchlistSubject>> ListSubjectsAsync(CancellationToken cancellationToken = default)
    {
        var entries = await _db.WatchlistEntries.AsNoTracking().ToListAsync(cancellationToken);
        return entries.Select(e => e.ToSubject()).ToList();
    }

    public Task MarkCheckedAsync(Guid loadId, DateTimeOffset checkedAt, CancellationToken cancellationToken = default)
        => _db.WatchlistLoads.Where(l => l.Id == loadId)
            .ExecuteUpdateAsync(u => u.SetProperty(l => l.CheckedAt, checkedAt), cancellationToken);

    public async Task ReplaceSourceAsync(WatchlistLoad load, IReadOnlyList<WatchlistSubject> subjects, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await _db.WatchlistEntries.Where(e => e.Source == load.Source).ExecuteDeleteAsync(cancellationToken);
        await _db.WatchlistLoads.AddAsync(load, cancellationToken);
        await _db.WatchlistEntries.AddRangeAsync(subjects.Select(s => WatchlistEntry.From(s, load.Id)), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _db.ChangeTracker.Clear();
    }
}

public sealed class TenantWatchlistRepository : ITenantWatchlistRepository
{
    private readonly AegisDbContext _db;

    public TenantWatchlistRepository(AegisDbContext db) => _db = db;

    public async Task<IReadOnlyList<WatchlistSubject>> ListSubjectsAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        var entries = await _db.TenantWatchlistEntries.AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        return entries.Select(e => e.ToSubject()).ToList();
    }

    public async Task<TenantWatchlistSummary> GetSummaryAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        var rows = _db.TenantWatchlistEntries.AsNoTracking().Where(e => e.TenantId == tenantId);
        var count = await rows.CountAsync(cancellationToken);
        var latest = await rows.OrderByDescending(e => e.CreatedAt)
            .Select(e => new { e.CreatedAt, e.UploadedBy })
            .FirstOrDefaultAsync(cancellationToken);
        return new TenantWatchlistSummary(count, latest?.CreatedAt, latest?.UploadedBy);
    }

    public async Task ReplaceAsync(TenantId tenantId, IReadOnlyList<TenantWatchlistEntry> entries, CancellationToken cancellationToken = default)
    {
        var existing = await _db.TenantWatchlistEntries.Where(e => e.TenantId == tenantId).ToListAsync(cancellationToken);
        _db.TenantWatchlistEntries.RemoveRange(existing);
        await _db.TenantWatchlistEntries.AddRangeAsync(entries, cancellationToken);
    }
}

public sealed class TenantScreeningStateRepository : ITenantScreeningStateRepository
{
    private readonly AegisDbContext _db;

    public TenantScreeningStateRepository(AegisDbContext db) => _db = db;

    public Task<string?> GetListVersionAsync(TenantId tenantId, CancellationToken cancellationToken = default)
        => _db.TenantScreeningStates.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.ListVersion)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task SetListVersionAsync(TenantId tenantId, string listVersion, CancellationToken cancellationToken = default)
    {
        var state = await _db.TenantScreeningStates.FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        if (state is null) await _db.TenantScreeningStates.AddAsync(TenantScreeningState.Create(tenantId, listVersion), cancellationToken);
        else state.Advance(listVersion);
    }
}

public sealed class ScreeningMatchRepository : IScreeningMatchRepository
{
    private readonly AegisDbContext _db;

    public ScreeningMatchRepository(AegisDbContext db) => _db = db;

    public Task<ScreeningMatch?> GetAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken = default)
        => _db.ScreeningMatches.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Id == id, cancellationToken);

    public async Task<IReadOnlySet<(WatchlistSource Source, string ExternalId)>> ListKeysAsync(
        TenantId tenantId, Guid customerId, CancellationToken cancellationToken = default)
    {
        var keys = await _db.ScreeningMatches.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.CustomerId == customerId)
            .Select(m => new { m.Source, m.ExternalId })
            .ToListAsync(cancellationToken);
        var local = _db.ScreeningMatches.Local
            .Where(m => m.TenantId == tenantId && m.CustomerId == customerId)
            .Select(m => new { m.Source, m.ExternalId });
        return keys.Concat(local).Select(k => (k.Source, k.ExternalId)).ToHashSet();
    }

    public async Task<PagedResult<ScreeningMatch>> ListAsync(TenantId tenantId, ScreeningMatchQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var rows = _db.ScreeningMatches.AsNoTracking().Where(m => m.TenantId == tenantId);
        if (query.Status is { } status) rows = rows.Where(m => m.Status == status);
        if (query.CustomerId is { } customerId) rows = rows.Where(m => m.CustomerId == customerId);

        var total = await rows.LongCountAsync(cancellationToken);
        var items = await rows.OrderByDescending(m => m.CreatedAt).ThenBy(m => m.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<ScreeningMatch> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }

    public Task<int> CountPendingAsync(TenantId tenantId, CancellationToken cancellationToken = default)
        => _db.ScreeningMatches.AsNoTracking()
            .CountAsync(m => m.TenantId == tenantId && m.Status == ScreeningMatchStatus.PENDING_REVIEW, cancellationToken);

    public async Task AddAsync(ScreeningMatch match, CancellationToken cancellationToken = default)
        => await _db.ScreeningMatches.AddAsync(match, cancellationToken);
}
