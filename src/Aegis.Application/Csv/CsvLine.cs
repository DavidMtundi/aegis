namespace Aegis.Application.Csv;

using System.Text;

/// <summary>RFC 4180 quoting within one physical line (records never span lines).</summary>
public static class CsvLine
{
    public static string[] Lines(string csv) => csv.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <returns>Null when a quoted field is not terminated on the same line.</returns>
    public static List<string>? Split(string line)
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
