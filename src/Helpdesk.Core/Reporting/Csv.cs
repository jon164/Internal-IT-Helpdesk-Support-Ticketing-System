namespace Helpdesk.Core.Reporting;

using System.Globalization;
using System.Text;

/// <summary>
/// USER STORY: As an IT manager, I want to export the dashboard summary, so that I can include
/// service data in reports to senior management.
///
/// Writes RFC 4180 CSV that opens correctly in Excel. Every dashboard export goes through here, so
/// quoting and the spreadsheet-formula guard cannot be forgotten in one of them.
///
/// Rules:
///   - A field containing a comma, a double quote, CR or LF is wrapped in double quotes, and any
///     double quote inside it is doubled. (Before this, a reporter called "Lee, Ana" split into
///     two columns and shifted every later column on that row.)
///   - Text starting with = + - @ tab or CR is prefixed with an apostrophe, so a spreadsheet shows
///     it as text instead of running it as a formula (OWASP "CSV injection"). Reporter names
///     are typed in by employees, so this is untrusted input. Plain numbers such as "-3" or
///     "1.5" are left alone, so numeric columns stay numeric.
///   - Lines end in CRLF on every operating system, as RFC 4180 requires.
///   - The file starts with a UTF-8 byte-order mark, so Excel reads names such as "Zoë" or
///     "Māori" correctly instead of guessing a legacy code page.
///   - Numbers are written with the invariant culture: always "12.5", never "12,5".
/// </summary>
public static class Csv
{
    public const string LineEnding = "\r\n";

    private static readonly char[] NeedsQuoting = [',', '"', '\r', '\n'];
    private static readonly char[] FormulaStart = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>One field, guarded and quoted as needed. Null becomes an empty field.</summary>
    public static string Field(string? value)
    {
        var text = value ?? string.Empty;

        if (text.Length > 0
            && Array.IndexOf(FormulaStart, text[0]) >= 0
            && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            text = "'" + text;
        }

        return text.IndexOfAny(NeedsQuoting) >= 0
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
    }

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A missing value (for example "no resolved tickets yet") is an empty field, not 0.</summary>
    public static string Number(double? value) =>
        value is null ? string.Empty : value.Value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// A complete CSV document: the header line, then one line per row. With no rows the result is
    /// the header line alone, which is still a valid file.
    /// </summary>
    public static string Document(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(",", header.Select(Field))).Append(LineEnding);

        foreach (var row in rows)
        {
            if (row.Count != header.Count)
            {
                throw new ArgumentException($"A CSV row has {row.Count} fields but the header has {header.Count}.", nameof(rows));
            }

            builder.Append(string.Join(",", row.Select(Field))).Append(LineEnding);
        }

        return builder.ToString();
    }

    /// <summary>The bytes to send: UTF-8 with a byte-order mark.</summary>
    public static byte[] ToBytes(string document) =>
        [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(document)];
}
