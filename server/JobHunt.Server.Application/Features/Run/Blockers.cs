namespace JobHunt.Server.Application.Features.Run;

/// <summary>
/// The day's APPLY-BLOCKERS list, in the existing shape: what stuck, and the one thing that clears it. A row the apply
/// lane blocked because the form needs Steve also names the control he has to answer, as the page labels it.
/// </summary>
public static class Blockers
{
    /// <summary>How the apply lane opens a reason when a form needs Steve; the control's name follows it.</summary>
    public const string NeedsSteve = "needs Steve:";

    /// <summary>The whole file.</summary>
    /// <param name="day">The Central day.</param>
    /// <param name="postings">The postings behind a wall.</param>
    public static string Render(DateOnly day, IReadOnlyList<Posting> postings)
    {
        var lines = new List<string>
        {
            "> RULES FIRST: read systemRules.md before acting on this file (MASTER RULE: if stuck or a tech glitch happens, retry, then reread the .md rules).",
            string.Empty,
            $"# APPLY-BLOCKERS {day:yyyy-MM-dd} ({RegistryRows.Lane})",
            string.Empty,
            "Each one: open the link, click Apply, sign in or create the account, stop on the first form page, say \"fill <company>\".",
            string.Empty,
        };
        int number = 0;
        foreach (Posting posting in postings)
        {
            lines.Add($"## {++number}. {posting.Company}, {posting.Role}");
            lines.Add($"- Where it stuck: {(posting.Board.Length > 0 ? posting.Board : "not a form this lane may use")}.");
            if (posting.Board.StartsWith(NeedsSteve, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"- The control: {posting.Board[NeedsSteve.Length..].Trim()}");
            }
            lines.Add($"- Link: {posting.Link}");
            lines.Add($"- Source: {posting.Source}");
            lines.Add(string.Empty);
        }
        return string.Join('\n', lines);
    }
}
