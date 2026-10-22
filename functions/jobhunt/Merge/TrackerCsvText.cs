using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;

namespace JobHunt.Functions.Merge;

/// <summary>
/// Reading and writing the tracker's CSV the way the laptop's merger always has: the same
/// parser (TextFieldParser, which handles quoted fields holding commas, quotes and newlines),
/// the same quoting rule on the way out, and CRLF line ends.
/// </summary>
public static partial class TrackerCsvText
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>UTF-8 text with any byte order mark removed.</summary>
    public static string Decode(byte[] bytes) =>
        HasBom(bytes) ? Utf8NoBom.GetString(bytes, 3, bytes.Length - 3) : Utf8NoBom.GetString(bytes);

    public static byte[] Encode(string text) => Utf8NoBom.GetBytes(text);

    public static bool HasBom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

    /// <summary>Every record that parses; a malformed line is reported through <paramref name="onError"/> and skipped.</summary>
    public static List<string[]> Parse(string text, Action<string>? onError = null)
    {
        var rows = new List<string[]>();
        using var parser = new TextFieldParser(new StringReader(text))
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false,
        };
        parser.SetDelimiters(",");
        while (!parser.EndOfData)
        {
            try
            {
                string[]? fields = parser.ReadFields();
                if (fields is not null) { rows.Add(fields); }
            }
            catch (MalformedLineException ex)
            {
                onError?.Invoke($"line {parser.ErrorLineNumber}: {ex.Message}");
            }
        }
        return rows;
    }

    /// <summary>A field quoted only when it holds a comma, a quote or a line break.</summary>
    public static string Field(string? value)
    {
        string s = value ?? string.Empty;
        return NeedsQuotes().IsMatch(s) ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    public static string Line(IEnumerable<string?> fields) => string.Join(",", fields.Select(Field));

    /// <summary>The digits of a Count cell ("3019" from "3019" or from a stray "#3019").</summary>
    public static string Digits(string? cell) => NonDigits().Replace(cell ?? string.Empty, string.Empty);

    /// <summary>The letters of a cell, to recognise a header row whatever its punctuation.</summary>
    public static string Letters(string? cell) => NonLetters().Replace(cell ?? string.Empty, string.Empty);

    /// <summary>The highest Count in the master and in the moved-out file, so a moved Count is never reused.</summary>
    public static int MaxCount(IEnumerable<string[]> rows)
    {
        int max = 0;
        foreach (string[] row in rows)
        {
            if (row.Length == 0) { continue; }
            if (int.TryParse(Digits(row[0]), out int n) && n > max) { max = n; }
        }
        return max;
    }

    [GeneratedRegex("[\",\r\n]")]
    private static partial Regex NeedsQuotes();

    [GeneratedRegex("[^\\d]")]
    private static partial Regex NonDigits();

    [GeneratedRegex("[^A-Za-z]")]
    private static partial Regex NonLetters();
}
