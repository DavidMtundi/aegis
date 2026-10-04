namespace Aegis.Tests.Unit.Screening;

using Aegis.Application.Screening;
using Aegis.Application.Risk;
using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Modules.Screening.Application;
using Aegis.Modules.Screening.Domain;
using Aegis.Shared.Contracts;
using Aegis.Shared.Domain;
using Aegis.Shared.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class CustomerScreeningServiceTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());

    [Fact]
    public async Task A_duplicate_in_a_batch_skips_only_that_customer()
    {
        var customers = Enumerable.Range(1, 3)
            .Select(i => new ScreeningCandidate(new Guid($"00000000-0000-0000-0000-00000000000{i}"), "Grace Akinyi Otieno", SubjectKind.INDIVIDUAL, null))
            .ToList();
        var duplicate = customers[1].CustomerId;
        var matches = new FakeMatches();
        var service = Service(customers, matches, new FakeUnitOfWork(matches, duplicate));

        var outcome = await service.ScreenTenantAsync(Tenant, ScreeningTriggers.ListUpdated, RiskActor.System);

        Assert.Equal(3, outcome.Screened);
        Assert.Equal(2, outcome.NewMatchIds.Count);
        Assert.Equal(new[] { customers[0].CustomerId, customers[2].CustomerId }, matches.Saved.Select(m => m.CustomerId).Order());
        Assert.Equal(matches.Saved.Select(m => m.Id).Order(), outcome.NewMatchIds.Order());
    }

    private static CustomerScreeningService Service(IReadOnlyList<ScreeningCandidate> customers, FakeMatches matches, IUnitOfWork uow)
    {
        var subject = new WatchlistSubject(
            WatchlistSource.TENANT, "PEP-1", WatchlistCategory.PEP, SubjectKind.INDIVIDUAL,
            new[] { "Grace Akinyi Otieno" }, Array.Empty<BirthYearRange>(), Array.Empty<string>(), null);
        return new CustomerScreeningService(
            new FakeCandidates(customers), new FakeGlobal(), new FakeTenantLists(subject), matches,
            null!, new FakeAudit(), uow, null!, new ScreeningOptions(), NullLogger<CustomerScreeningService>.Instance);
    }

    private sealed class FakeCandidates(IReadOnlyList<ScreeningCandidate> customers) : IScreeningCandidateReader
    {
        public Task<ScreeningCandidate?> GetAsync(TenantId tenantId, Guid customerId, CancellationToken cancellationToken = default)
            => Task.FromResult(customers.FirstOrDefault(c => c.CustomerId == customerId));

        public Task<IReadOnlyList<ScreeningCandidate>> ListAsync(TenantId tenantId, Guid? afterId, int take, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ScreeningCandidate>>(customers
                .Where(c => afterId is null || c.CustomerId.CompareTo(afterId.Value) > 0)
                .OrderBy(c => c.CustomerId).Take(take).ToList());
    }

    private sealed class FakeGlobal : IGlobalWatchlist
    {
        public Task<WatchlistIndex> GetAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WatchlistIndex(Array.Empty<WatchlistSubject>()));
    }

    private sealed class FakeTenantLists(WatchlistSubject subject) : ITenantWatchlistRepository
    {
        public Task<IReadOnlyList<WatchlistSubject>> ListSubjectsAsync(TenantId tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WatchlistSubject>>(new[] { subject });

        public Task<TenantWatchlistSummary> GetSummaryAsync(TenantId tenantId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ReplaceAsync(TenantId tenantId, IReadOnlyList<TenantWatchlistEntry> entries, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeMatches : IScreeningMatchRepository
    {
        public List<ScreeningMatch> Pending { get; } = new();
        public List<ScreeningMatch> Saved { get; } = new();

        public Task<ScreeningMatch?> GetAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlySet<(WatchlistSource Source, string ExternalId)>> ListKeysAsync(
            TenantId tenantId, Guid customerId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlySet<(WatchlistSource, string)>>(Saved.Concat(Pending)
                .Where(m => m.CustomerId == customerId).Select(m => (m.Source, m.ExternalId)).ToHashSet());

        public Task<PagedResult<ScreeningMatch>> ListAsync(TenantId tenantId, ScreeningMatchQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> CountPendingAsync(TenantId tenantId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task AddAsync(ScreeningMatch match, CancellationToken cancellationToken = default)
        {
            Pending.Add(match);
            return Task.CompletedTask;
        }
    }

    /// <summary>Behaves like EfUnitOfWork when another writer already stored a match for <paramref name="duplicate"/>.</summary>
    private sealed class FakeUnitOfWork(FakeMatches matches, Guid duplicate) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (matches.Pending.Any(m => m.CustomerId == duplicate))
            {
                matches.Pending.Clear();
                throw new UniqueConstraintViolationException("IX_screening_matches_tenant_id_customer_id_Source_ExternalId", new Exception());
            }
            var saved = matches.Pending.Count;
            matches.Saved.AddRange(matches.Pending);
            matches.Pending.Clear();
            return Task.FromResult(saved);
        }
    }

    private sealed class FakeAudit : IAuditWriter
    {
        public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
