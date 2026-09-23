namespace Aegis.Tests.Unit.Alerts;

using Aegis.Modules.Alerts.Application;
using Aegis.Modules.Alerts.Domain;
using Aegis.Modules.Aml.Domain;
using Aegis.Modules.Aml.Engine;
using Aegis.Shared.Domain;

public sealed class AlertDeduplicationTests
{
    private sealed class InMemoryAlertRepository : IAlertRepository
    {
        public List<Alert> Items { get; } = new();

        public Task AddAsync(Alert alert, CancellationToken cancellationToken = default)
        {
            Items.Add(alert);
            return Task.CompletedTask;
        }

        public Task<Alert?> GetByTenantAndDeduplicationKeyAsync(TenantId tenantId, string deduplicationKey, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(a => a.TenantId == tenantId && a.DeduplicationKey == deduplicationKey));

        public Task<Alert?> GetByTenantAndIdAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(a => a.TenantId == tenantId && a.Id == id));

        public Task<IReadOnlyList<Alert>> ListByTenantAsync(TenantId tenantId, int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Alert>>(Items.Where(a => a.TenantId == tenantId).Take(take).ToList());

        public Task<AlertListResult> ListByTenantAsync(TenantId tenantId, AlertListQuery query, CancellationToken cancellationToken = default)
        {
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);
            IEnumerable<Alert> filtered = Items.Where(a => a.TenantId == tenantId);
            if (query.Status is AlertStatus status)
                filtered = filtered.Where(a => a.Status == status);
            if (query.Severity is AlertSeverity severity)
                filtered = filtered.Where(a => a.Severity == severity);
            if (query.From is DateTimeOffset from)
                filtered = filtered.Where(a => a.TriggeredAt >= from);
            if (query.To is DateTimeOffset to)
                filtered = filtered.Where(a => a.TriggeredAt <= to);
            var list = filtered.OrderByDescending(a => a.TriggeredAt).ToList();
            var items = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(new AlertListResult(items, list.Count, page, pageSize));
        }
    }

    private static RuleEvaluationResult Triggered(Guid ruleId, Guid versionId, string focusEntityId) => new()
    {
        RuleId = ruleId,
        RuleVersionId = versionId,
        RuleCode = "STRUCTURING_001",
        RuleName = "Structuring",
        RuleVersion = 1,
        FocusEntityId = focusEntityId,
        FocusEntityType = FocusType.CUSTOMER.ToString(),
        IsTriggered = true,
        ConditionResults = new List<ConditionEvaluationResult>
        {
            new(true, "transaction_count_24h >= 5", 7, ">=", 5)
        }
    };

    [Fact]
    public void Deduplication_key_includes_rule_version_and_utc_day_from_bucket_timestamp()
    {
        var tenantId = new TenantId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var ruleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var versionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var focus = "44444444-4444-4444-4444-444444444444";
        var ts = new DateTimeOffset(2026, 9, 23, 22, 15, 0, TimeSpan.FromHours(3)); // UTC date still 2026-09-23

        var key = AlertDeduplicationKey.Build(tenantId, ruleId, versionId, FocusType.CUSTOMER, focus, ts);

        Assert.Equal(
            "11111111-1111-1111-1111-111111111111:22222222-2222-2222-2222-222222222222:33333333-3333-3333-3333-333333333333:CUSTOMER:44444444-4444-4444-4444-444444444444:2026-09-23",
            key);
    }

    [Fact]
    public async Task CreateOrGet_dedupes_same_key_and_creates_for_new_rule_version()
    {
        var repo = new InMemoryAlertRepository();
        var service = new AlertService(repo);
        var tenantId = new TenantId(Guid.NewGuid());
        var ruleId = Guid.NewGuid();
        var version1 = Guid.NewGuid();
        var version2 = Guid.NewGuid();
        var customerId = Guid.NewGuid().ToString();
        var day = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        var evidence = new AlertEvidence
        {
            RuleName = "Structuring",
            RuleVersionNumber = 1,
            TransactionIds = new List<string> { Guid.NewGuid().ToString() }
        };

        var first = await service.CreateOrGetAsync(
            tenantId, Triggered(ruleId, version1, customerId), evidence, AlertSeverity.HIGH, 80, day);
        var second = await service.CreateOrGetAsync(
            tenantId, Triggered(ruleId, version1, customerId), evidence, AlertSeverity.HIGH, 80, day);
        var third = await service.CreateOrGetAsync(
            tenantId, Triggered(ruleId, version2, customerId), evidence with { RuleVersionNumber = 2 }, AlertSeverity.HIGH, 80, day);

        Assert.True(first.WasCreated);
        Assert.False(second.WasCreated);
        Assert.Equal(first.AlertId, second.AlertId);
        Assert.True(third.WasCreated);
        Assert.NotEqual(first.AlertId, third.AlertId);
        Assert.Equal(2, repo.Items.Count);
    }
}
