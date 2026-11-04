using System.Text.RegularExpressions;

namespace JobHunt.Server.Application.Features.Run;

/// <summary>
/// Reads the progress.tsv a lane keeps, the record of a send that survives a kill. It is read loosely, because a small
/// model writes it, and any line it cannot read is handed back, because a send nobody can read is worse than a send
/// nobody wrote down.
/// </summary>
public static partial class ProgressFile
{
    /// <summary>The sends in the file, one per row, and the lines that speak of a send and could not be read.</summary>
    /// <param name="text">The file's text.</param>
    public static (IReadOnlyList<RowResult> Rows, IReadOnlyList<string> Unread) Read(string text)
    {
        var rows = new List<RowResult>();
        var unread = new List<string>();
        foreach (string line in text.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            Match sent = Line().Match(line);
            if (sent.Success && int.TryParse(sent.Groups["row"].Value, out int row))
            {
                string said = sent.Groups["person"].Value.Trim();
                if (rows.FirstOrDefault(known => known.Row == row) is { } first)
                {
                    if (PersonGate.Name(first.Person ?? string.Empty) != PersonGate.Name(said))
                    {
                        unread.Add(line.Trim());
                    }
                }
                else
                {
                    string person = sent.Groups["person"].Value.Trim();
                    rows.Add(new RowResult(row, "sent", "from progress.tsv", person.Length > 0 ? person : null, null, null, null));
                }
            }
            else if (line.Trim().Length > 0 && !Header().IsMatch(line))
            {
                unread.Add(line.Trim());
            }
        }
        return (rows, unread);
    }

    [GeneratedRegex(@"^\s*(?<row>\d+)(?:[\s|,:;]|<TAB>)+(?:sent|submitted|applied|invited|messaged)(?:(?:[\s|,:;]|<TAB>)+(?<person>.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Line();

    [GeneratedRegex(@"^\s*row(\t|<TAB>)", RegexOptions.IgnoreCase)]
    private static partial Regex Header();
}
