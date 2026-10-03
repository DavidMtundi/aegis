namespace Aegis.Application.Risk;

using Aegis.Modules.Risk.Domain;
using Aegis.Shared.Domain;

public static class RiskBatchSchedule
{
    /// <summary>Next occurrence of <paramref name="hourUtc"/>:00 UTC strictly after <paramref name="now"/>.</summary>
    public static DateTimeOffset NextRun(DateTimeOffset now, int hourUtc)
    {
        var utc = now.ToUniversalTime();
        var hour = Math.Clamp(hourUtc, 0, 23);
        var today = new DateTimeOffset(utc.Year, utc.Month, utc.Day, hour, 0, 0, TimeSpan.Zero);
        return today > utc ? today : today.AddDays(1);
    }
}

/// <summary>Enumerates what the batch scores. Keyset paging keeps pages stable while customers are being added.</summary>
public interface IRiskBatchSource
{
    Task<IReadOnlyList<Guid>> ListScorableTenantIdsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> ListCustomerIdsAsync(TenantId tenantId, Guid? afterId, int take, CancellationToken cancellationToken = default);
}

public interface IRiskBatchRecalculator
{
    /// <returns>Number of customers scored.</returns>
    Task<int> RecalculateTenantAsync(TenantId tenantId, RiskActor actor, CancellationToken cancellationToken = default);
}

public sealed class RiskBatchRecalculator : IRiskBatchRecalculator
{
    private const int PageSize = 200;

    private readonly IRiskBatchSource _source;
    private readonly ICustomerRiskService _risk;

    public RiskBatchRecalculator(IRiskBatchSource source, ICustomerRiskService risk)
    {
        _source = source;
        _risk = risk;
    }

    public async Task<int> RecalculateTenantAsync(TenantId tenantId, RiskActor actor, CancellationToken cancellationToken = default)
    {
        var scored = 0;
        Guid? after = null;
        while (true)
        {
            var ids = await _source.ListCustomerIdsAsync(tenantId, after, PageSize, cancellationToken);
            foreach (var id in ids)
            {
                if (await _risk.TryRecalculateAsync(tenantId, id, RiskTriggers.Batch, actor, cancellationToken) is not null)
                    scored++;
            }

            if (ids.Count < PageSize) return scored;
            after = ids[^1];
        }
    }
}
