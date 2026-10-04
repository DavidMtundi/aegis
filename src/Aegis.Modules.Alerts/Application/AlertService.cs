namespace Aegis.Modules.Alerts.Application;

using Aegis.Modules.Alerts.Domain;
using Aegis.Modules.Aml.Engine;
using Aegis.Shared.Domain;

public sealed class AlertService : IAlertService
{
    /// <summary>Longest stretch over which a still-true windowed condition is treated as the same activity.</summary>
    public static readonly TimeSpan MaxSuppression = TimeSpan.FromDays(7);

    private readonly IAlertRepository _alerts;

    public AlertService(IAlertRepository alerts) => _alerts = alerts;

    public async Task<AlertUpsertResult> CreateOrGetAsync(
        TenantId tenantId,
        RuleEvaluationResult triggered,
        AlertEvidence evidence,
        AlertSeverity severity,
        int riskScore,
        DateTimeOffset bucketTimestamp,
        TimeSpan lookback = default,
        CancellationToken cancellationToken = default)
    {
        if (!triggered.IsTriggered)
        {
            throw new InvalidOperationException("CreateOrGetAsync requires a triggered evaluation result.");
        }

        if (!Enum.TryParse<FocusType>(triggered.FocusEntityType, true, out var focusType))
        {
            focusType = FocusType.CUSTOMER;
        }

        var key = AlertDeduplicationKey.Build(
            tenantId,
            triggered.RuleId,
            triggered.RuleVersionId,
            focusType,
            triggered.FocusEntityId,
            bucketTimestamp);

        var existing = await _alerts.GetByTenantAndDeduplicationKeyAsync(tenantId, key, cancellationToken);
        if (existing is not null)
        {
            return new AlertUpsertResult(existing.Id, WasCreated: false);
        }

        var suppressFrom = (bucketTimestamp - (lookback > MaxSuppression ? MaxSuppression : lookback)).UtcDateTime.Date;
        for (var day = bucketTimestamp.UtcDateTime.Date.AddDays(-1); day >= suppressFrom; day = day.AddDays(-1))
        {
            var earlierKey = AlertDeduplicationKey.Build(
                tenantId, triggered.RuleId, triggered.RuleVersionId, focusType, triggered.FocusEntityId,
                new DateTimeOffset(day, TimeSpan.Zero));
            var earlier = await _alerts.GetByTenantAndDeduplicationKeyAsync(tenantId, earlierKey, cancellationToken);
            if (earlier is not null)
            {
                return new AlertUpsertResult(earlier.Id, WasCreated: false);
            }
        }

        var alert = Alert.Create(
            tenantId,
            triggered.RuleId,
            triggered.RuleVersionId,
            focusType,
            triggered.FocusEntityId,
            severity,
            riskScore,
            evidence,
            key);

        await _alerts.AddAsync(alert, cancellationToken);
        return new AlertUpsertResult(alert.Id, WasCreated: true);
    }
}

public static class AlertEvidenceMapper
{
    public static AlertEvidence FromEvaluation(
        RuleEvaluationResult result,
        IReadOnlyList<string> transactionIds,
        IReadOnlyDictionary<string, object> features)
    {
        var conditions = result.ConditionResults
            .Where(c => c.Satisfied)
            .Select(c => c.ConditionDescription)
            .ToList();

        var facts = result.ConditionResults.ToDictionary(
            c => c.ConditionDescription,
            c => (object)new Dictionary<string, object?>
            {
                ["actual"] = c.ActualValue,
                ["operator"] = c.Operator,
                ["expected"] = c.ExpectedValue,
                ["satisfied"] = c.Satisfied
            });

        return new AlertEvidence
        {
            RuleName = result.RuleName,
            RuleVersionNumber = result.RuleVersion,
            EvaluatedValues = features.ToDictionary(k => k.Key, v => v.Value),
            ConditionsSatisfied = conditions,
            TransactionIds = transactionIds.ToList(),
            AdditionalContext = new Dictionary<string, object>
            {
                ["ruleCode"] = result.RuleCode,
                ["ruleVersionId"] = result.RuleVersionId.ToString("D"),
                ["conditionFacts"] = facts
            }
        };
    }
}
