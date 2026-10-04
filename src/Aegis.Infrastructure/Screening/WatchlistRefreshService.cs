namespace Aegis.Infrastructure.Screening;

using Aegis.Application.Risk;
using Aegis.Application.Screening;
using Aegis.Modules.Screening.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Loads any list that has never been loaded at startup, then refreshes UN and OFAC daily. When a list changed it
/// wakes <see cref="ScreeningRescreenService"/>, which rescreens the tenants.
/// Settings: Screening:Refresh:Enabled (default true) and Screening:Refresh:HourUtc (default 1).
/// </summary>
public sealed class WatchlistRefreshService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IRescreenSignal _rescreen;
    private readonly IConfiguration _config;
    private readonly ILogger<WatchlistRefreshService> _logger;

    public WatchlistRefreshService(
        IServiceScopeFactory scopes, IRescreenSignal rescreen, IConfiguration config, ILogger<WatchlistRefreshService> logger)
    {
        _scopes = scopes;
        _rescreen = rescreen;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue("Screening:Refresh:Enabled", true))
        {
            _logger.LogInformation("Watchlist refresh disabled");
            return;
        }

        try
        {
            await RunOnceAsync(onlyMissing: true, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initial watchlist load failed");
        }

        var hourUtc = _config.GetValue("Screening:Refresh:HourUtc", 1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RiskBatchSchedule.NextRun(DateTimeOffset.UtcNow, hourUtc) - DateTimeOffset.UtcNow, stoppingToken);
                await RunOnceAsync(onlyMissing: false, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watchlist refresh failed");
            }
        }
    }

    private async Task RunOnceAsync(bool onlyMissing, CancellationToken ct)
    {
        var changed = false;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            var refresher = scope.ServiceProvider.GetRequiredService<IWatchlistRefresher>();
            var lists = scope.ServiceProvider.GetRequiredService<IWatchlistRepository>();
            foreach (var source in refresher.Sources)
            {
                if (onlyMissing && await lists.GetLatestLoadAsync(source, ct) is not null) continue;
                changed |= (await refresher.RefreshAsync(source, ct)).Status == WatchlistRefreshStatus.LOADED;
            }
        }
        if (changed) _rescreen.Request();
    }
}
