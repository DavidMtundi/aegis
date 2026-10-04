namespace Aegis.Application.Screening;

using Aegis.Application.Risk;
using Aegis.Modules.Screening.Application;
using Aegis.Modules.Screening.Domain;
using Aegis.Shared.Domain;
using Aegis.Shared.Persistence;

/// <summary>Wakes the rescreen worker now instead of at its next scheduled pass.</summary>
public interface IRescreenSignal
{
    void Request();

    ValueTask WaitAsync(CancellationToken cancellationToken);
}

public interface IScreeningRescreener
{
    /// <summary>
    /// Rescreens every customer of the tenant when the lists changed since its last full screen, then records the
    /// new list version. The version only moves once the whole run has saved.
    /// </summary>
    /// <returns>Null when the tenant was already screened against the current lists.</returns>
    Task<ScreeningOutcome?> RescreenIfStaleAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

public sealed class ScreeningRescreener : IScreeningRescreener
{
    private readonly IWatchlistRepository _lists;
    private readonly ITenantWatchlistRepository _tenantLists;
    private readonly ITenantScreeningStateRepository _states;
    private readonly ICustomerScreeningService _screening;
    private readonly IUnitOfWork _uow;

    public ScreeningRescreener(
        IWatchlistRepository lists,
        ITenantWatchlistRepository tenantLists,
        ITenantScreeningStateRepository states,
        ICustomerScreeningService screening,
        IUnitOfWork uow)
    {
        _lists = lists;
        _tenantLists = tenantLists;
        _states = states;
        _screening = screening;
        _uow = uow;
    }

    public async Task<ScreeningOutcome?> RescreenIfStaleAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        // Read before screening: a list that changes mid-run leaves the tenant stale for the next pass.
        var version = ScreeningListVersion.Compute(
            await _lists.ListLatestLoadsAsync(cancellationToken), await _tenantLists.GetSummaryAsync(tenantId, cancellationToken));
        if (await _states.GetListVersionAsync(tenantId, cancellationToken) == version) return null;

        var outcome = await _screening.ScreenTenantAsync(tenantId, ScreeningTriggers.ListUpdated, RiskActor.System, cancellationToken);
        await _states.SetListVersionAsync(tenantId, version, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return outcome;
    }
}
