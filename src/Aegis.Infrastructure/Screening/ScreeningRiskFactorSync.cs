namespace Aegis.Infrastructure.Screening;

using Aegis.Application.Risk;
using Aegis.Infrastructure.Persistence;
using Aegis.Modules.Risk.Domain;
using Aegis.Shared.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Gives tenants whose risk model predates screening the three screening factors, as a new audited version.
/// A tenant that ever had one of them is skipped, so an admin who later removes a factor keeps that choice.
/// </summary>
public sealed class ScreeningRiskFactorSync
{
    private static readonly RiskFactorType[] Types =
        { RiskFactorType.SANCTIONS_MATCH, RiskFactorType.PEP_MATCH, RiskFactorType.WATCHLIST_MATCH };

    private readonly AegisDbContext _db;
    private readonly ICustomerRiskService _risk;

    public ScreeningRiskFactorSync(AegisDbContext db, ICustomerRiskService risk)
    {
        _db = db;
        _risk = risk;
    }

    public async Task<int> SyncAsync(TenantId? onlyTenant = null, CancellationToken cancellationToken = default)
    {
        var query = _db.RiskModels.AsNoTracking();
        if (onlyTenant is { } tenant) query = query.Where(m => m.TenantId == tenant);
        var models = await query.ToListAsync(cancellationToken);
        var alreadyHad = models.Where(m => m.Factors.Any(f => Types.Contains(f.Type))).Select(m => m.TenantId).ToHashSet();
        var defaults = RiskModelDefaults.Factors.Where(f => Types.Contains(f.Type)).ToList();
        var actor = new RiskActor("system-seed", null, null);

        var updated = 0;
        foreach (var active in models.Where(m => m.IsActive && !alreadyHad.Contains(m.TenantId)))
        {
            await _risk.UpdateModelAsync(active.TenantId, active.Factors.Concat(defaults).ToList(), active.Bands, actor, cancellationToken);
            updated++;
        }
        return updated;
    }
}

public sealed class ScreeningRiskFactorSyncService : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<ScreeningRiskFactorSyncService> _logger;

    public ScreeningRiskFactorSyncService(
        IServiceScopeFactory scopes, IConfiguration config, ILogger<ScreeningRiskFactorSyncService> logger)
    {
        _scopes = scopes;
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_config.GetValue("Risk:SyncScreeningFactorsOnStartup", true)) return;
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ScreeningRiskFactorSync>().SyncAsync(null, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Adding screening factors to existing risk models failed; the API starts without them");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
