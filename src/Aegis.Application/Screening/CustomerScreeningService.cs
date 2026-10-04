namespace Aegis.Application.Screening;

using Aegis.Application.Risk;
using Aegis.Modules.Alerts.Application;
using Aegis.Modules.Alerts.Domain;
using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Modules.Risk.Domain;
using Aegis.Modules.Screening.Application;
using Aegis.Modules.Screening.Domain;
using Aegis.Shared.Domain;
using Aegis.Shared.Persistence;
using Microsoft.Extensions.Logging;

public enum ScreeningDecision { CONFIRM, FALSE_POSITIVE }

public sealed record ScreeningOutcome(int Screened, IReadOnlyList<Guid> NewMatchIds);

public interface ICustomerScreeningService
{
    /// <returns>Null when the customer does not exist in the tenant.</returns>
    Task<ScreeningOutcome?> ScreenCustomerAsync(
        TenantId tenantId, Guid customerId, string trigger, RiskActor actor, CancellationToken cancellationToken = default);

    /// <summary><see cref="ScreenCustomerAsync"/> for side-effect triggers: a failure is logged and never fails the caller.</summary>
    Task TryScreenCustomerAsync(
        TenantId tenantId, Guid customerId, string trigger, RiskActor actor, CancellationToken cancellationToken = default);

    Task<ScreeningOutcome> ScreenTenantAsync(TenantId tenantId, string trigger, RiskActor actor, CancellationToken cancellationToken = default);

    /// <returns>Null when the match does not exist in the tenant.</returns>
    /// <exception cref="ArgumentException">The reason is blank.</exception>
    /// <exception cref="ScreeningMatchAlreadyReviewedException">The match was already reviewed, possibly concurrently.</exception>
    Task<ScreeningMatch?> ReviewAsync(
        TenantId tenantId, Guid matchId, ScreeningDecision decision, string reason, RiskActor actor,
        CancellationToken cancellationToken = default);
}

public sealed class CustomerScreeningService : ICustomerScreeningService
{
    private const int BatchSize = 500;

    private readonly IScreeningCandidateReader _candidates;
    private readonly IGlobalWatchlist _global;
    private readonly ITenantWatchlistRepository _tenantLists;
    private readonly IScreeningMatchRepository _matches;
    private readonly IAlertRepository _alerts;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly ICustomerRiskService _risk;
    private readonly ScreeningOptions _options;
    private readonly ILogger<CustomerScreeningService> _logger;

    public CustomerScreeningService(
        IScreeningCandidateReader candidates,
        IGlobalWatchlist global,
        ITenantWatchlistRepository tenantLists,
        IScreeningMatchRepository matches,
        IAlertRepository alerts,
        IAuditWriter audit,
        IUnitOfWork uow,
        ICustomerRiskService risk,
        ScreeningOptions options,
        ILogger<CustomerScreeningService> logger)
    {
        _candidates = candidates;
        _global = global;
        _tenantLists = tenantLists;
        _matches = matches;
        _alerts = alerts;
        _audit = audit;
        _uow = uow;
        _risk = risk;
        _options = options;
        _logger = logger;
    }

    public async Task<ScreeningOutcome?> ScreenCustomerAsync(
        TenantId tenantId, Guid customerId, string trigger, RiskActor actor, CancellationToken cancellationToken = default)
    {
        var candidate = await _candidates.GetAsync(tenantId, customerId, cancellationToken);
        if (candidate is null) return null;

        var created = await ScreenAsync(tenantId, candidate, await ListsAsync(tenantId, cancellationToken), trigger, actor, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return new ScreeningOutcome(1, created);
    }

    public async Task TryScreenCustomerAsync(
        TenantId tenantId, Guid customerId, string trigger, RiskActor actor, CancellationToken cancellationToken = default)
    {
        try
        {
            await ScreenCustomerAsync(tenantId, customerId, trigger, actor, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Screening ({Trigger}) failed for customer {CustomerId}", trigger, customerId);
        }
    }

    public async Task<ScreeningOutcome> ScreenTenantAsync(
        TenantId tenantId, string trigger, RiskActor actor, CancellationToken cancellationToken = default)
    {
        var lists = await ListsAsync(tenantId, cancellationToken);
        var created = new List<Guid>();
        var screened = 0;
        Guid? after = null;
        while (true)
        {
            var batch = await _candidates.ListAsync(tenantId, after, BatchSize, cancellationToken);
            if (batch.Count == 0) break;
            foreach (var candidate in batch)
                created.AddRange(await ScreenAsync(tenantId, candidate, lists, trigger, actor, cancellationToken));
            await _uow.SaveChangesAsync(cancellationToken);
            screened += batch.Count;
            after = batch[^1].CustomerId;
        }
        return new ScreeningOutcome(screened, created);
    }

    public async Task<ScreeningMatch?> ReviewAsync(
        TenantId tenantId, Guid matchId, ScreeningDecision decision, string reason, RiskActor actor,
        CancellationToken cancellationToken = default)
    {
        var match = await _matches.GetAsync(tenantId, matchId, cancellationToken);
        if (match is null) return null;
        var before = match.Status;

        if (decision == ScreeningDecision.CONFIRM)
        {
            var (severity, riskScore, title) = ScreeningAlertPolicy.For(match.Category);
            var alertId = Guid.NewGuid();
            match.Confirm(actor.Id, reason, alertId);
            var alert = Alert.CreateFromScreening(
                tenantId, match.CustomerId, match.Id, severity, riskScore, ScreeningAlertPolicy.Evidence(match, title), alertId);
            await _alerts.AddAsync(alert, cancellationToken);
            await _audit.AppendAsync(AuditEvent.Create(
                tenantId.Value, AuditEventTypes.ALERT_CREATED, nameof(Alert), alert.Id.ToString(), actor.Id, actor.Role, null,
                AuditPayload.Json(new { ruleCode = ScreeningAlertSource.RuleCode, severity = severity.ToString(), screeningMatchId = match.Id }),
                $"{title}: {match.MatchedName}", actor.CorrelationId), cancellationToken);
        }
        else
        {
            match.MarkFalsePositive(actor.Id, reason);
        }

        await _audit.AppendAsync(AuditEvent.Create(
            tenantId.Value, AuditEventTypes.SCREENING_MATCH_RESOLVED, nameof(ScreeningMatch), match.Id.ToString(), actor.Id, actor.Role,
            AuditPayload.Json(new { status = before.ToString() }),
            AuditPayload.Json(new { status = match.Status.ToString(), reason = match.ReviewReason, alertId = match.AlertId }),
            decision == ScreeningDecision.CONFIRM ? "Screening match confirmed" : "Screening match marked false positive",
            actor.CorrelationId), cancellationToken);
        try
        {
            await _uow.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException ex)
        {
            throw new ScreeningMatchAlreadyReviewedException("This match was reviewed by someone else. Reload to see the decision.", ex);
        }

        if (decision == ScreeningDecision.CONFIRM)
            await _risk.TryRecalculateAsync(tenantId, match.CustomerId, RiskTriggers.ScreeningConfirmed, actor, cancellationToken);
        return match;
    }

    private async Task<WatchlistIndex[]> ListsAsync(TenantId tenantId, CancellationToken cancellationToken)
        => new[]
        {
            await _global.GetAsync(cancellationToken),
            new WatchlistIndex(await _tenantLists.ListSubjectsAsync(tenantId, cancellationToken))
        };

    private async Task<IReadOnlyList<Guid>> ScreenAsync(
        TenantId tenantId, ScreeningCandidate candidate, WatchlistIndex[] lists, string trigger, RiskActor actor,
        CancellationToken cancellationToken)
    {
        var found = lists.SelectMany(l => l.Screen(candidate, _options.MatchThreshold)).ToList();
        if (found.Count == 0) return Array.Empty<Guid>();

        var known = await _matches.ListKeysAsync(tenantId, candidate.CustomerId, cancellationToken);
        var created = new List<Guid>();
        foreach (var potential in found)
        {
            if (known.Contains((potential.Subject.Source, potential.Subject.ExternalId))) continue;
            var match = ScreeningMatch.Create(tenantId, candidate, potential, trigger);
            await _matches.AddAsync(match, cancellationToken);
            await _audit.AppendAsync(AuditEvent.Create(
                tenantId.Value, AuditEventTypes.SCREENING_MATCH_CREATED, nameof(ScreeningMatch), match.Id.ToString(), actor.Id, actor.Role, null,
                AuditPayload.Json(new
                {
                    customerId = candidate.CustomerId,
                    source = potential.Subject.Source.ToString(),
                    externalId = potential.Subject.ExternalId,
                    matchedName = potential.MatchedName,
                    score = potential.Score
                }),
                $"Possible {potential.Subject.Category} match: {potential.MatchedName}", actor.CorrelationId), cancellationToken);
            created.Add(match.Id);
        }
        return created;
    }
}
