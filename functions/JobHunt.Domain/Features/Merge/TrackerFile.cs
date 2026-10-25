using System.Security.Cryptography;
using System.Text;

namespace JobHunt.Domain.Features.Merge;

// #region tracker-file
/// <summary>
/// Job_Hunt_Tracker.csv as records, cut where the file cuts them, so it can be kept elsewhere and put back byte for
/// byte (PROMPT-JOBHUNT-TRACKER-SQL, rung 1). A record ends at a CRLF outside quotes; a line break inside a quoted
/// field, CRLF or LF alone, stays in its record. The byte order mark and whether the file ends with a break are kept
/// beside the records, because the file has both and a reader nobody is watching may depend on either.
/// </summary>
/// <param name="Bom">True when the file opens with the UTF-8 byte order mark.</param>
/// <param name="Records">The header record, then every row's record, each as the file writes it, without its break.</param>
/// <param name="EndsWithBreak">True when the last record is followed by a CRLF.</param>
public sealed record TrackerFile(bool Bom, IReadOnlyList<string> Records, bool EndsWithBreak)
{
    /// <summary>The break between records.</summary>
    public const string Break = "\r\n";

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>The file's records, cut at every CRLF that is not inside quotes.</summary>
    /// <param name="bytes">The file as read.</param>
    public static TrackerFile Split(byte[] bytes)
    {
        bool bom = TrackerCsvText.HasBom(bytes);
        string text = TrackerCsvText.Decode(bytes);
        var records = new List<string>();
        bool quoted = false;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                records.Add(text[start..i]);
                start = i + 2;
                i++;
            }
        }
        bool ends = start == text.Length && records.Count > 0;
        if (!ends)
        {
            records.Add(text[start..]);
        }
        return new TrackerFile(bom, records, ends);
    }

    /// <summary>The file again, byte for byte.</summary>
    public byte[] ToBytes()
    {
        string text = string.Join(Break, Records) + (EndsWithBreak ? Break : string.Empty);
        byte[] body = Utf8NoBom.GetBytes(text);
        return Bom ? [0xEF, 0xBB, 0xBF, .. body] : body;
    }

    /// <summary>Lower-case hex SHA-256 of a record's UTF-8 bytes.</summary>
    /// <param name="record">The record.</param>
    public static string Hash(string record) => Convert.ToHexStringLower(SHA256.HashData(Utf8NoBom.GetBytes(record)));

    /// <summary>Lower-case hex SHA-256 of the whole file.</summary>
    /// <param name="bytes">The file.</param>
    public static string FileHash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
// #endregion tracker-file
