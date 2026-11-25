namespace JobHunt.Server.Domain.Shared.Worklists;

/// <summary>
/// The apply list's fresh supply: the roles the night's picker passed (nightly/jobs-candidates-&lt;day&gt;.tsv, above its
/// FAILED line) that were posted or first seen in the last three days, read from the employer's own board, with no hold
/// the jobs lane must clear first. They follow the jobs lane's READY rows, and every gate the 6 AM list holds a READY
/// row to (applied, RULE 50, an account wall, one a company a morning) holds them too. A title the one title test
/// (RoleTitle) fails stays out, so a picker file written before the pick read it brings no people, product or sales seat.
/// </summary>
public static class FreshPostings
{
    /// <summary>How fresh a picker row must be, in days since posted or first seen.</summary>
    public const int FreshDays = 3;

    /// <summary>The picker's candidates file for a day, from the share's root.</summary>
    /// <param name="day">The Central day.</param>
    public static string PathFor(DateOnly day) => $"nightly/jobs-candidates-{day:yyyy-MM-dd}.tsv";

    /// <summary>The fresh passed rows as postings, in the file's order.</summary>
    /// <param name="name">The file's path, for each row's source.</param>
    /// <param name="tsv">The file's text.</param>
    /// <param name="today">The Central day.</param>
    public static IReadOnlyList<Posting> Read(string name, string tsv, DateOnly today)
    {
        string[] lines = tsv.Replace("\r", string.Empty).Split('\n');
        int header = Array.FindIndex(lines, line => line.StartsWith("Source\t", StringComparison.Ordinal));
        if (header < 0)
        {
            return [];
        }
        var column = lines[header].Split('\t').Select((cell, i) => (cell, i)).ToDictionary(c => c.cell, c => c.i, StringComparer.Ordinal);
        string[] needed = ["Company", "Title", "Url", "BoardKind", "Posted", "Confirmed", "Hold"];
        if (needed.Any(cell => !column.ContainsKey(cell)))
        {
            return [];
        }
        var postings = new List<Posting>();
        for (int i = header + 1; i < lines.Length && !lines[i].StartsWith('#'); i++)
        {
            string[] cells = lines[i].Split('\t');
            if (cells.Length < column.Count)
            {
                continue;
            }
            string posted = cells[column["Posted"]];
            bool fresh = posted.Length >= 10 && DateOnly.TryParseExact(posted[..10], "yyyy-MM-dd", out DateOnly day) && today.DayNumber - day.DayNumber <= FreshDays;
            if (fresh && cells[column["Confirmed"]] == "feed" && cells[column["Hold"]].Trim().Length == 0 && RoleTitle.IsEngineering(cells[column["Title"]]))
            {
                postings.Add(new Posting(cells[column["Company"]], cells[column["Title"]], cells[column["Url"]], cells[column["BoardKind"]], $"{name}:{i + 1}", posted));
            }
        }
        return postings;
    }
}
