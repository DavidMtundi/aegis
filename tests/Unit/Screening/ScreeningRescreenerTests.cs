namespace Aegis.Tests.Unit.Screening;

using Aegis.Application.Risk;
using Aegis.Application.Screening;
using Aegis.Modules.Screening.Application;
using Aegis.Modules.Screening.Domain;
using Aegis.Shared.Domain;
using Aegis.Shared.Persistence;

public sealed class ScreeningRescreenerTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly WatchlistLoad UnLoad = WatchlistLoad.Record(WatchlistSource.UN, null, 4, "hash-1");
    private static readonly TenantWatchlistSummary NoTenantList = new(0, null, null);
    private static readonly GlobalWatchlistSnapshot Global = new(
        new WatchlistIndex(Array.Empty<WatchlistSubject>()), ScreeningListVersion.Global(new[] { UnLoad }));

    [Fact]
    public async Task A_tenant_never_screened_is_screened_and_its_version_recorded()
    {
        var states = new FakeStates();
        var screening = new FakeScreening();
        var uow = new CountingUnitOfWork();

        var outcome = await Rescreener(states, screening, uow).RescreenIfStaleAsync(Tenant, Global);

        Assert.NotNull(outcome);
        Assert.Equal(1, screening.Calls);
        Assert.Equal(ScreeningListVersion.Compute(Global.Version, NoTenantList), states.Version);
        Assert.Equal(1, uow.Saves);
    }

    [Fact]
    public async Task The_tenant_is_screened_against_the_snapshot_whose_version_is_recorded()
    {
        var screening = new FakeScreening();

        await Rescreener(new FakeStates(), screening, new CountingUnitOfWork()).RescreenIfStaleAsync(Tenant, Global);

        Assert.Same(Global, screening.Snapshot);
    }

    [Fact]
    public async Task A_tenant_on_the_current_version_is_skipped()
    {
        var states = new FakeStates { Version = ScreeningListVersion.Compute(Global.Version, NoTenantList) };
        var screening = new FakeScreening();

        Assert.Null(await Rescreener(states, screening, new CountingUnitOfWork()).RescreenIfStaleAsync(Tenant, Global));
        Assert.Equal(0, screening.Calls);
    }

    [Fact]
    public async Task A_failed_run_keeps_the_old_version()
    {
        var states = new FakeStates { Version = "UN:old" };
        var screening = new FakeScreening { Failure = new TimeoutException("database timeout") };

        await Assert.ThrowsAsync<TimeoutException>(() => Rescreener(states, screening, new CountingUnitOfWork()).RescreenIfStaleAsync(Tenant, Global));
        Assert.Equal("UN:old", states.Version);
    }

    [Fact]
    public void A_new_upload_or_load_changes_the_version()
    {
        var uploaded = new TenantWatchlistSummary(3, DateTimeOffset.UtcNow, "admin");
        var global = ScreeningListVersion.Global(new[] { UnLoad });
        var baseline = ScreeningListVersion.Compute(global, uploaded);

        Assert.NotEqual(baseline, ScreeningListVersion.Compute(global, uploaded with { UploadedAt = uploaded.UploadedAt!.Value.AddSeconds(1) }));
        Assert.NotEqual(baseline, ScreeningListVersion.Compute(
            ScreeningListVersion.Global(new[] { WatchlistLoad.Record(WatchlistSource.UN, null, 4, "hash-1") }), uploaded));
        Assert.Equal(baseline, ScreeningListVersion.Compute(ScreeningListVersion.Global(new[] { UnLoad }), uploaded));
    }

    private static ScreeningRescreener Rescreener(FakeStates states, FakeScreening screening, IUnitOfWork uow)
        => new(new FakeTenantLists(), states, screening, uow);

    private sealed class FakeTenantLists : ITenantWatchlistRepository
    {
        public Task<IReadOnlyList<WatchlistSubject>> ListSubjectsAsync(TenantId tenantId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TenantWatchlistSummary> GetSummaryAsync(TenantId tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(NoTenantList);

        public Task ReplaceAsync(TenantId tenantId, IReadOnlyList<TenantWatchlistEntry> entries, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeStates : ITenantScreeningStateRepository
    {
        public string? Version { get; set; }

        public Task<string?> GetListVersionAsync(TenantId tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(Version);

        public Task SetListVersionAsync(TenantId tenantId, string listVersion, CancellationToken cancellationToken = default)
        {
            Version = listVersion;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeScreening : ICustomerScreeningService
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }
        public GlobalWatchlistSnapshot? Snapshot { get; private set; }

        public Task<ScreeningOutcome> ScreenTenantAsync(
            TenantId tenantId, GlobalWatchlistSnapshot global, string trigger, RiskActor actor, CancellationToken cancellationToken = default)
        {
            Calls++;
            Snapshot = global;
            if (Failure is not null) throw Failure;
            return Task.FromResult(new ScreeningOutcome(0, Array.Empty<Guid>()));
        }

        public Task<ScreeningOutcome?> ScreenCustomerAsync(TenantId tenantId, Guid customerId, string trigger, RiskActor actor, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task TryScreenCustomerAsync(TenantId tenantId, Guid customerId, string trigger, RiskActor actor, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ScreeningMatch?> ReviewAsync(
            TenantId tenantId, Guid matchId, ScreeningDecision decision, string reason, RiskActor actor, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }
}
