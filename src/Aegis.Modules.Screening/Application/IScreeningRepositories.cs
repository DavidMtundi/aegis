namespace Aegis.Modules.Screening.Application;

using Aegis.Modules.Screening.Domain;
using Aegis.Shared.Contracts;
using Aegis.Shared.Domain;

public interface IWatchlistRepository
{
    /// <summary>Newest load per source.</summary>
    Task<IReadOnlyList<WatchlistLoad>> ListLatestLoadsAsync(CancellationToken cancellationToken = default);

    Task<WatchlistLoad?> GetLatestLoadAsync(WatchlistSource source, CancellationToken cancellationToken = default);

    /// <summary>Time of the newest load of any source; changes whenever a list is replaced.</summary>
    Task<DateTimeOffset?> GetLastLoadedAtAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WatchlistSubject>> ListSubjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces every entry of the load's source and records the load, in one transaction. Saves immediately.</summary>
    Task ReplaceSourceAsync(WatchlistLoad load, IReadOnlyList<WatchlistSubject> subjects, CancellationToken cancellationToken = default);
}

public sealed record TenantWatchlistSummary(int EntryCount, DateTimeOffset? UploadedAt, string? UploadedBy);

public interface ITenantWatchlistRepository
{
    Task<IReadOnlyList<WatchlistSubject>> ListSubjectsAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    Task<TenantWatchlistSummary> GetSummaryAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>Removes the tenant's entries and adds <paramref name="entries"/>; the caller saves.</summary>
    Task ReplaceAsync(TenantId tenantId, IReadOnlyList<TenantWatchlistEntry> entries, CancellationToken cancellationToken = default);
}

public sealed record ScreeningMatchQuery(ScreeningMatchStatus? Status = null, Guid? CustomerId = null, int Page = 1, int PageSize = 50);

public interface IScreeningMatchRepository
{
    Task<ScreeningMatch?> GetAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Source and external id of every match already recorded for the customer, in any status.</summary>
    Task<IReadOnlySet<(WatchlistSource Source, string ExternalId)>> ListKeysAsync(
        TenantId tenantId, Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<PagedResult<ScreeningMatch>> ListAsync(TenantId tenantId, ScreeningMatchQuery query, CancellationToken cancellationToken = default);

    Task<int> CountPendingAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    Task AddAsync(ScreeningMatch match, CancellationToken cancellationToken = default);
}
