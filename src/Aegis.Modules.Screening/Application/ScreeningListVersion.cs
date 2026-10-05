namespace Aegis.Modules.Screening.Application;

using System.Globalization;
using Aegis.Modules.Screening.Domain;

/// <summary>
/// Identifies the lists a tenant screens against: the newest load of each global list plus the tenant's own upload.
/// Any refresh that loads a new file, or any upload, gives a different value.
/// </summary>
public static class ScreeningListVersion
{
    /// <summary>The global part: the newest load id of each source.</summary>
    public static string Global(IEnumerable<WatchlistLoad> latestLoads)
        => string.Join(";", latestLoads.OrderBy(l => l.Source).Select(l => $"{l.Source}:{l.Id:N}"));

    public static string Compute(string globalVersion, TenantWatchlistSummary tenantList)
    {
        var tenant = string.Create(CultureInfo.InvariantCulture,
            $"{WatchlistSource.TENANT}:{tenantList.EntryCount}@{tenantList.UploadedAt?.UtcTicks ?? 0}");
        return globalVersion.Length == 0 ? tenant : $"{globalVersion};{tenant}";
    }
}
