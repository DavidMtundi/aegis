namespace Aegis.Modules.Alerts.Application;

using Aegis.Modules.Alerts.Domain;
using Aegis.Modules.Aml.Engine;
using Aegis.Shared.Domain;

public sealed class AlertService : IAlertService
{
    private readonly IAlertRepository _alerts;

    public AlertService(IAlertRepository alerts) => _alerts = alerts;

    public async Task<AlertUpsertResult> CreateOrGetAsync(
        TenantId tenantId,
        RuleEvaluationResult triggered,
        AlertEvidence evidence,
        AlertSeverity severity,
        int riskScore,
        DateTimeOffset bucketTimestamp,
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
