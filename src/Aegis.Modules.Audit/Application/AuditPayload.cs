namespace Aegis.Modules.Audit.Application;

using System.Text.Json;

public static class AuditPayload
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Json(object value) => JsonSerializer.Serialize(value, Options);
}
