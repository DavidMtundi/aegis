namespace Aegis.Infrastructure.Screening;

using System.Threading.Channels;
using Aegis.Application.Risk;
using Aegis.Application.Screening;
using Aegis.Shared.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>Coalesces requests: any number of signals before the worker wakes cause one pass.</summary>
public sealed class RescreenSignal : IRescreenSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Request() => _channel.Writer.TryWrite(true);

    public async ValueTask WaitAsync(CancellationToken cancellationToken) => await _channel.Reader.ReadAsync(cancellationToken);
}

/// <summary>
/// Rescreens the customers of every non-suspended tenant whose lists changed since its last full screen, whichever
/// process loaded the new list or accepted the upload. Runs every Screening:Rescreen:IntervalMinutes (default 15) and
/// as soon as <see cref="IRescreenSignal"/> is requested. Screening:Rescreen:Enabled (default true) turns the loop off.
/// </summary>
public sealed class ScreeningRescreenService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IRescreenSignal _signal;
    private readonly IGlobalWatchlist _global;
    private readonly IConfiguration _config;
    private readonly ILogger<ScreeningRescreenService> _logger;
    private readonly SemaphoreSlim _running = new(1, 1);

    public ScreeningRescreenService(
        IServiceScopeFactory scopes, IRescreenSignal signal, IGlobalWatchlist global, IConfiguration config,
        ILogger<ScreeningRescreenService> logger)
    {
        _scopes = scopes;
        _signal = signal;
        _global = global;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue("Screening:Rescreen:Enabled", true))
        {
            _logger.LogInformation("Screening rescreen worker disabled");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _config.GetValue("Screening:Rescreen:IntervalMinutes", 15)));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Screening rescreen pass failed");
            }

            using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            wait.CancelAfter(interval);
            try
            {
                await _signal.WaitAsync(wait.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                // The interval elapsed without a signal.
            }
        }
    }

    /// <summary>
    /// One pass over every scorable tenant against one global list snapshot. A failing tenant is logged and keeps its
    /// old list version; a list loaded mid-pass is picked up by the next pass.
    /// </summary>
    /// <returns>Number of tenants rescreened.</returns>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await _running.WaitAsync(cancellationToken);
        try
        {
            IReadOnlyList<Guid> tenantIds;
            await using (var scope = _scopes.CreateAsyncScope())
            {
                tenantIds = await scope.ServiceProvider.GetRequiredService<IRiskBatchSource>().ListScorableTenantIdsAsync(cancellationToken);
            }
            var global = await _global.GetAsync(cancellationToken);

            var rescreened = 0;
            foreach (var tenantId in tenantIds)
            {
                try
                {
                    if (await RescreenTenantAsync(new TenantId(tenantId), global, cancellationToken)) rescreened++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Rescreen failed for tenant {TenantId}", tenantId);
                }
            }
            return rescreened;
        }
        finally
        {
            _running.Release();
        }
    }

    /// <returns>False when the tenant was already screened against the current lists.</returns>
    public async Task<bool> RescreenTenantAsync(TenantId tenantId, CancellationToken cancellationToken)
        => await RescreenTenantAsync(tenantId, await _global.GetAsync(cancellationToken), cancellationToken);

    private async Task<bool> RescreenTenantAsync(TenantId tenantId, GlobalWatchlistSnapshot global, CancellationToken cancellationToken)
    {
        // A scope per tenant keeps the change tracker small and isolates failures.
        await using var scope = _scopes.CreateAsyncScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<IScreeningRescreener>()
            .RescreenIfStaleAsync(tenantId, global, cancellationToken);
        if (outcome is null) return false;
        _logger.LogInformation("Rescreened {Count} customers for tenant {TenantId}: {Matches} new matches",
            outcome.Screened, tenantId.Value, outcome.NewMatchIds.Count);
        return true;
    }
}
