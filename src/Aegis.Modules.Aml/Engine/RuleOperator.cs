using System.Collections.Generic;

namespace Aegis.Modules.Aml.Engine;

public static class RuleOperator
{
    // Comparison
    public const string Equal = "=";
    public const string NotEqual = "!=";
    public const string GreaterThan = ">";
    public const string GreaterThanOrEqual = ">=";
    public const string LessThan = "<";
    public const string LessThanOrEqual = "<=";
    public const string Between = "BETWEEN";
    
    // Set
    public const string In = "IN";
    public const string NotIn = "NOT_IN";
    
    // String
    public const string Contains = "CONTAINS";
    public const string StartsWith = "STARTS_WITH";
    
    // Null
    public const string IsNull = "IS_NULL";
    public const string IsNotNull = "IS_NOT_NULL";

    private static readonly HashSet<string> _validOperators = new HashSet<string>
    {
        Equal, NotEqual, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, Between,
        In, NotIn, Contains, StartsWith, IsNull, IsNotNull
    };

    public static bool IsValid(string op) => op != null && _validOperators.Contains(op.ToUpperInvariant());
}
