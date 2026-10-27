using System.Text;

namespace Tasky.Services.DataExchange;

/// <summary>
/// Reads and writes comma-separated files the way Excel, Google Sheets and Todoist do
/// (RFC 4180: quoted fields may contain commas, quotes and line breaks).
/// </summary>
public static class CsvFormat
{
    private const char Separator = ',';
    private const char Quote     = '"';

    /// <summary>Excel only detects UTF-8 (and so shows "é", "ü", emoji correctly) with this marker.</summary>
    public static readonly Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static string Write(IEnumerable<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            for (int i = 0; i < row.Count; i++)
            {
                if (i > 0) sb.Append(Separator);
                sb.Append(Escape(row[i]));
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    private static string Escape(string field)
        => field.IndexOfAny([Separator, Quote, '\r', '\n']) < 0 && field.Trim() == field
            ? field
            : Quote + field.Replace("\"", "\"\"") + Quote;

    /// <summary>Splits CSV text into rows of fields. Empty lines are skipped.</summary>
    public static List<List<string>> Read(string text)
    {
        var rows  = new List<List<string>>();
        var row   = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        void EndField() { row.Add(field.ToString()); field.Clear(); }
        void EndRow()
        {
            EndField();
            if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
            row = new List<string>();
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c != Quote)               field.Append(c);
                else if (Peek(text, i) == Quote) { field.Append(Quote); i++; }
                else                          inQuotes = false;
                continue;
            }
            switch (c)
            {
                case Quote:     inQuotes = true; break;
                case Separator: EndField();      break;
                case '\r':      break;
                case '\n':      EndRow();        break;
                default:        field.Append(c); break;
            }
        }
        if (field.Length > 0 || row.Count > 0) EndRow();
        return rows;
    }

    private static char? Peek(string text, int index) => index + 1 < text.Length ? text[index + 1] : null;
}
