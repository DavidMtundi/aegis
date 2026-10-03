namespace Aegis.Application.Lookups;

using Aegis.Shared.Domain;

public sealed record CustomerLabel(Guid Id, string Name, string Country, string Type);

/// <summary>Batch resolution of ids to display labels so list responses avoid one query per row.</summary>
public interface IDisplayNameLookup
{
    Task<IReadOnlyDictionary<Guid, CustomerLabel>> CustomersAsync(
        TenantId tenantId, IEnumerable<Guid> customerIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(
        TenantId tenantId, IEnumerable<Guid> userIds, CancellationToken cancellationToken = default);
}

public static class DisplayIds
{
    /// <summary>Alert focus ids and assignee ids are stored as strings; anything that isn't a Guid is skipped.</summary>
    public static IEnumerable<Guid> Parse(IEnumerable<string?> values)
    {
        foreach (var value in values)
        {
            if (Guid.TryParse(value, out var id)) yield return id;
        }
    }

    public static TValue? Find<TValue>(IReadOnlyDictionary<Guid, TValue> map, string? id) where TValue : class
        => Guid.TryParse(id, out var guid) && map.TryGetValue(guid, out var value) ? value : null;
}
