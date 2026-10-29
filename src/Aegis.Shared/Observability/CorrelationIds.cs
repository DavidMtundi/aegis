namespace Aegis.Shared.Observability;

public static class CorrelationIds
{
    public const string HeaderName = "X-Correlation-ID";
    public const string AlternateHeaderName = "X-Request-ID";
    public const int MaxLength = 100;

    public static string Normalize(string? incoming, string? fallbackTraceId)
    {
        var candidate = FirstNonEmpty(incoming, fallbackTraceId);
        if (string.IsNullOrWhiteSpace(candidate))
            return Guid.NewGuid().ToString("N");

        candidate = candidate.Trim();
        return candidate.Length <= MaxLength ? candidate : candidate[..MaxLength];
    }

    public static string? FirstHeader(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? FirstNonEmpty(params string?[] values) => FirstHeader(values);
}
