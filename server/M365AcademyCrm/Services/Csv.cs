using System.Text;

namespace M365AcademyCrm.Services;

/// <summary>Minimal RFC 4180 CSV writing and reading - enough for exports and simple imports, no dependency.</summary>
public static class Csv
{
    public static byte[] Write(IEnumerable<string> header, IEnumerable<IEnumerable<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", header.Select(Escape)));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",", row.Select(v => Escape(Format(v)))));
        }

        // BOM so Excel opens UTF-8 (names with accents, the £ sign) correctly.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm"),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        decimal m => m.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string Escape(string value)
    {
        // Leading =, +, -, @ would be run as a formula by Excel - CSV injection.
        if (value.Length > 0 && "=+-@".Contains(value[0]) && !decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    /// <summary>Parses CSV text into rows of fields, honouring quoted fields with embedded commas, quotes and newlines.</summary>
    public static List<List<string>> Read(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                if (row.Any(f => f.Length > 0))
                {
                    rows.Add(row);
                }

                row = new List<string>();
            }
            else
            {
                field.Append(c);
            }
        }

        row.Add(field.ToString());
        if (row.Any(f => f.Length > 0))
        {
            rows.Add(row);
        }

        return rows;
    }
}
