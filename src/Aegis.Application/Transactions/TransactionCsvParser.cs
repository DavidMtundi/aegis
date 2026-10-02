namespace Aegis.Application.Transactions;

using System.Globalization;
using System.Text;

public sealed record CsvTransactionRow(int LineNumber, string? ExternalReference, IngestTransactionPayload? Payload, string? Error);

public sealed record CsvParseResult(IReadOnlyList<string> HeaderErrors, IReadOnlyList<CsvTransactionRow> Rows);

/// <summary>
/// Header-driven CSV (RFC 4180 quoting, one record per line). Numbers and dates use the invariant culture.
/// </summary>
public static class TransactionCsvParser
{
    private static readonly string[] Required =
    {
        "externalReference", "accountId", "customerId", "amount", "currency",
        "direction", "transactionType", "channel", "timestamp"
    };

    private const string CounterpartyCountry = "counterpartyCountry";

    public static CsvParseResult Parse(string csv)
    {
        var lines = csv.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var headerIndex = Array.FindIndex(lines, l => !string.IsNullOrWhiteSpace(l));
        if (headerIndex < 0)
            return new CsvParseResult(new[] { "CSV is empty." }, Array.Empty<CsvTransactionRow>());

        var header = SplitLine(lines[headerIndex]);
        if (header is null)
            return new CsvParseResult(new[] { "Header row has an unterminated quote." }, Array.Empty<CsvTransactionRow>());

        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
            columns.TryAdd(header[i].Trim(), i);

        var missing = Required.Where(r => !columns.ContainsKey(r)).ToList();
        if (missing.Count > 0)
            return new CsvParseResult(
                new[] { $"Missing required columns: {string.Join(", ", missing)}." },
                Array.Empty<CsvTransactionRow>());

        var rows = new List<CsvTransactionRow>();
        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            rows.Add(ParseRow(i + 1, lines[i], header.Count, columns));
        }

        return new CsvParseResult(Array.Empty<string>(), rows);
    }

    private static CsvTransactionRow ParseRow(int lineNumber, string line, int columnCount, Dictionary<string, int> columns)
    {
        var fields = SplitLine(line);
        if (fields is null)
            return new CsvTransactionRow(lineNumber, null, null, "Unterminated quote.");

        string? Get(string name) => columns.TryGetValue(name, out var idx) && idx < fields.Count
            ? fields[idx].Trim()
            : null;

        var reference = Get("externalReference");
        if (fields.Count != columnCount)
            return new CsvTransactionRow(lineNumber, reference, null, $"Expected {columnCount} columns but found {fields.Count}.");

        var errors = new List<string>();
        if (string.IsNullOrEmpty(reference)) errors.Add("externalReference is required");
        if (!Guid.TryParse(Get("accountId"), out var accountId)) errors.Add("accountId must be a GUID");
        if (!Guid.TryParse(Get("customerId"), out var customerId)) errors.Add("customerId must be a GUID");
        if (!decimal.TryParse(Get("amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            errors.Add("amount must be a number");
        if (!DateTimeOffset.TryParse(Get("timestamp"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
            errors.Add("timestamp must be an ISO 8601 date-time");
        foreach (var name in new[] { "currency", "direction", "transactionType", "channel" })
        {
            if (string.IsNullOrEmpty(Get(name))) errors.Add($"{name} is required");
        }

        if (errors.Count > 0)
            return new CsvTransactionRow(lineNumber, reference, null, string.Join("; ", errors) + ".");

        var counterparty = Get(CounterpartyCountry);
        return new CsvTransactionRow(lineNumber, reference, new IngestTransactionPayload(
            reference!,
            accountId,
            customerId,
            amount,
            Get("currency")!,
            Get("direction")!,
            Get("transactionType")!,
            Get("channel")!,
            timestamp,
            string.IsNullOrEmpty(counterparty) ? null : counterparty,
            null), null);
    }

    /// <returns>Null when a quoted field is not terminated on the same line.</returns>
    private static List<string>? SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        if (inQuotes) return null;
        fields.Add(current.ToString());
        return fields;
    }
}
