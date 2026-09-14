namespace Aegis.Shared.Domain;

/// <summary>
/// The type of entity that an AML rule is focused on.
/// Used across the AML, Alert, and Case modules.
/// </summary>
public enum FocusType
{
    CUSTOMER,
    ACCOUNT,
    BUSINESS,
    HOUSEHOLD
}
