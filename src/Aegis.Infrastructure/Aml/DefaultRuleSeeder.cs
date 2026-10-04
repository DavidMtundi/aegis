namespace Aegis.Infrastructure.Aml;

using Aegis.Modules.Aml.Application;
using Aegis.Modules.Aml.Domain;
using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Shared.Domain;

public sealed class DefaultRuleSeeder : IDefaultRuleSeeder
{
    public const string Actor = "system-seed";

    private readonly IAmlRuleRepository _rules;
    private readonly IAmlRuleVersionRepository _versions;
    private readonly IAuditWriter _audit;

    public DefaultRuleSeeder(IAmlRuleRepository rules, IAmlRuleVersionRepository versions, IAuditWriter audit)
    {
        _rules = rules;
        _versions = versions;
        _audit = audit;
    }

    public async Task<IReadOnlyList<string>> EnsureSeededAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        var added = new List<string>();
        foreach (var entry in DefaultRuleCatalog.All)
        {
            if (await _rules.GetByTenantAndCodeAsync(tenantId, entry.Code, cancellationToken) is not null) continue;

            var rule = AmlRule.CreateDraft(tenantId, entry.Code, entry.Name, entry.Description, entry.Scenario, Actor);
            rule.Approve();
            rule.Activate();
            var version = AmlRuleVersion.CreateActive(rule.Id, tenantId, 1, entry.Definition, Actor, DateTimeOffset.UtcNow);

            await _rules.AddAsync(rule, cancellationToken);
            await _versions.AddAsync(version, cancellationToken);
            await _audit.AppendAsync(AuditEvent.Create(
                tenantId.Value,
                AuditEventTypes.RULE_ACTIVATED,
                nameof(AmlRuleVersion),
                version.Id.ToString(),
                Actor,
                null,
                null,
                AuditPayload.Json(new { ruleId = rule.Id, code = entry.Code, version = 1 }),
                "Default rule added",
                null), cancellationToken);
            added.Add(entry.Code);
        }

        return added;
    }
}
