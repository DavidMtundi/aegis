namespace Aegis.Modules.Features.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aegis.Shared.Domain;

public interface IFeatureCalculator
{
    Task<FeatureCalculationResult> CalculateAsync(
        TenantId tenantId,
        FocusType focusType,
        string focusEntityId,
        DateTimeOffset asOfTimestamp,
        TimeSpan window,
        CancellationToken cancellationToken = default);
}

public sealed record FeatureCalculationResult(
    IReadOnlyDictionary<string, object> Features,
    IReadOnlyList<string> TransactionIds);
