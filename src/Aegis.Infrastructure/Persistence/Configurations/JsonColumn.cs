namespace Aegis.Infrastructure.Persistence.Configurations;

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;

internal static class JsonColumn
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Read<T>(string json) where T : new() => JsonSerializer.Deserialize<T>(json, Options) ?? new T();

    public static ValueComparer<T> Comparer<T>() where T : new() => new(
        (a, b) => Write(a) == Write(b),
        v => Write(v).GetHashCode(),
        v => Read<T>(Write(v)));
}
