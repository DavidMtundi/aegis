namespace Aegis.Infrastructure.Risk;

using Aegis.Application.Risk;
using Aegis.Shared.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Rescores every customer of every non-suspended tenant once a day so time-windowed factors decay.
/// Settings: Risk:NightlyBatch:Enabled (default true) and Risk:NightlyBatch:HourUtc (default 2).
/// </summary>
public sealed class NightlyRiskBatchService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<NightlyRiskBatchService> _logger;

    public NightlyRiskBatchService(IServiceScopeFactory scopes, IConfiguration config, ILogger<NightlyRiskBatchService> logger)
    {
        _scopes = scopes;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue("Risk:NightlyBatch:Enabled", true))
        {
            _logger.LogInformation("Nightly risk batch disabled");
            return;
        }

        var hourUtc = _config.GetValue("Risk:NightlyBatch:HourUtc", 2);
        while (!stoppingToken.IsCancellationRequested)
        {
            var next = RiskBatchSchedule.NextRun(DateTimeOffset.UtcNow, hourUtc);
            try
            {
                await Task.Delay(next - DateTimeOffset.UtcNow, stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Nightly risk batch failed");
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> tenantIds;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            tenantIds = await scope.ServiceProvider.GetRequiredService<IRiskBatchSource>().ListScorableTenantIdsAsync(ct);
        }

        foreach (var tenantId in tenantIds)
        {
            // A scope per tenant keeps the change tracker small and isolates failures.
            await using var scope = _scopes.CreateAsyncScope();
            try
            {
                var scored = await scope.ServiceProvider.GetRequiredService<IRiskBatchRecalculator>()
                    .RecalculateTenantAsync(new TenantId(tenantId), RiskActor.System, ct);
                _logger.LogInformation("Nightly risk batch scored {Count} customers for tenant {TenantId}", scored, tenantId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Nightly risk batch failed for tenant {TenantId}", tenantId);
            }
        }
    }
}
