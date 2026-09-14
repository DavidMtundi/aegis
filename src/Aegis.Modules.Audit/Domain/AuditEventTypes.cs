namespace Aegis.Modules.Audit.Domain;

public static class AuditEventTypes
{
    public const string RULE_CREATED = nameof(RULE_CREATED);
    public const string RULE_UPDATED = nameof(RULE_UPDATED);
    public const string RULE_SUBMITTED = nameof(RULE_SUBMITTED);
    public const string RULE_APPROVED = nameof(RULE_APPROVED);
    public const string RULE_ACTIVATED = nameof(RULE_ACTIVATED);
    public const string RULE_DISABLED = nameof(RULE_DISABLED);
    public const string RULE_RETIRED = nameof(RULE_RETIRED);
    
    public const string ALERT_CREATED = nameof(ALERT_CREATED);
    public const string ALERT_ASSIGNED = nameof(ALERT_ASSIGNED);
    public const string ALERT_RESOLVED = nameof(ALERT_RESOLVED);
    public const string ALERT_DISMISSED = nameof(ALERT_DISMISSED);
    public const string ALERT_ESCALATED = nameof(ALERT_ESCALATED);
    
    public const string CASE_CREATED = nameof(CASE_CREATED);
    public const string CASE_ASSIGNED = nameof(CASE_ASSIGNED);
    public const string CASE_UPDATED = nameof(CASE_UPDATED);
    public const string CASE_ESCALATED = nameof(CASE_ESCALATED);
    public const string CASE_DECIDED = nameof(CASE_DECIDED);
    public const string CASE_CLOSED = nameof(CASE_CLOSED);
    
    public const string RISK_SCORE_CHANGED = nameof(RISK_SCORE_CHANGED);
    public const string SCREENING_MATCH_CREATED = nameof(SCREENING_MATCH_CREATED);
    public const string SCREENING_MATCH_RESOLVED = nameof(SCREENING_MATCH_RESOLVED);
    
    public const string CUSTOMER_CREATED = nameof(CUSTOMER_CREATED);
    public const string CUSTOMER_UPDATED = nameof(CUSTOMER_UPDATED);
    public const string ACCOUNT_CREATED = nameof(ACCOUNT_CREATED);
    public const string TRANSACTION_INGESTED = nameof(TRANSACTION_INGESTED);
    public const string TRANSACTION_REJECTED = nameof(TRANSACTION_REJECTED);
}
