namespace Aegis.Infrastructure.Aml;

using Aegis.Infrastructure.Persistence;
using Aegis.Modules.Aml.Application;
using Aegis.Modules.Identity.Domain;
using Aegis.Shared.Domain;
using Aegis.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// On startup, gives every non-suspended tenant any default rule it is missing, so catalog additions reach
/// existing tenants. Setting: Aml:SyncDefaultRulesOnStartup (default true).
/// </summary>
public sealed class DefaultRuleSyncService : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<DefaultRuleSyncService> _logger;

    public DefaultRuleSyncService(IServiceScopeFactory scopes, IConfiguration config, ILogger<DefaultRuleSyncService> logger)
    {
        _scopes = scopes;
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_config.GetValue("Aml:SyncDefaultRulesOnStartup", true)) return;

        List<Guid> tenantIds;
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AegisDbContext>();
            var codes = DefaultRuleCatalog.All.Select(r => r.Code).ToList();
            var present = await db.AmlRules.AsNoTracking()
                .Where(r => codes.Contains(r.Code))
                .Select(r => r.TenantId)
                .ToListAsync(cancellationToken);
            var complete = present.GroupBy(t => t.Value).Where(g => g.Count() >= codes.Count).Select(g => g.Key).ToHashSet();
            tenantIds = (await db.Tenants.AsNoTracking()
                    .Where(t => t.Status != TenantStatus.SUSPENDED)
                    .Select(t => t.Id)
                    .ToListAsync(cancellationToken))
                .Where(id => !complete.Contains(id))
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Default rule sync skipped: tenants could not be listed");
            return;
        }

        foreach (var tenantId in tenantIds)
        {
            await using var scope = _scopes.CreateAsyncScope();
            try
            {
                var added = await scope.ServiceProvider.GetRequiredService<IDefaultRuleSeeder>()
                    .EnsureSeededAsync(new TenantId(tenantId), cancellationToken);
                if (added.Count == 0) continue;
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Added default rules {Codes} to tenant {TenantId}", string.Join(", ", added), tenantId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Default rule sync failed for tenant {TenantId}", tenantId);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
