namespace Aegis.Shared.Domain;

/// <summary>
/// Severity level for an AML alert.
/// Used across the AML, Alert, and Case modules.
/// </summary>
public enum AlertSeverity
{
    LOW,
    MEDIUM,
    HIGH,
    CRITICAL
}
