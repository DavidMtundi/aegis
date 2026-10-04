namespace Aegis.Modules.Screening.Application;

using System.Globalization;
using Aegis.Modules.Screening.Domain;

/// <summary>
/// Identifies the lists a tenant screens against: the newest load of each global list plus the tenant's own upload.
/// Any refresh that loads a new file, or any upload, gives a different value.
/// </summary>
public static class ScreeningListVersion
{
    public static string Compute(IEnumerable<WatchlistLoad> latestLoads, TenantWatchlistSummary tenantList)
    {
        var parts = latestLoads.OrderBy(l => l.Source).Select(l => $"{l.Source}:{l.Id:N}").ToList();
        parts.Add(string.Create(CultureInfo.InvariantCulture,
            $"{WatchlistSource.TENANT}:{tenantList.EntryCount}@{tenantList.UploadedAt?.UtcTicks ?? 0}"));
        return string.Join(";", parts);
    }
}
