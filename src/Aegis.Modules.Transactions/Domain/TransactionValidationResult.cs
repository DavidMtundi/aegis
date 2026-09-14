namespace Aegis.Modules.Transactions.Domain;

using System.Collections.Generic;

/// <summary>
/// Represents the result of validating a transaction.
/// </summary>
public sealed record TransactionValidationResult
{
    public TransactionQualityStatus QualityStatus { get; init; }
    public List<string> Errors { get; init; } = new();
    public bool IsValid { get; init; }
}
