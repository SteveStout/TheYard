namespace JobHunt.Server.Application.Features.Run;

/// <summary>
/// The job link step of the invite worklist (JobLinkNote): run on the finished list, so the builder keeps its five
/// ports. A note is rebuilt when the tracker holds an Application row at the person's company with a posting link,
/// the board still lists it, and the rebuilt note passes the same checks the queued one did; otherwise the queued
/// note goes out as it is. The newest such row wins. Each rebuilt row's expected count is the rebuilt note's.
/// </summary>
/// <param name="postings">The boards.</param>
/// <param name="prose">check_aiisms.py, which the rebuilt note passes like the queued one.</param>
public sealed class JobLinks(IPostingCheck postings, IProseCheck prose)
{
    /// <summary>The invite worklist with each note that can carry its job link rebuilt to carry it.</summary>
    /// <param name="reads">The morning's reads, for the tracker rows.</param>
    /// <param name="building">The worklist as the builder made it.</param>
    /// <param name="ct">Stops the reads.</param>
    public async Task<Worklist> ApplyAsync(DayReads reads, Task<Worklist> building, CancellationToken ct)
    {
        Worklist list = await building;
        var texts = list.Texts.ToDictionary();
        var rows = new List<WorkRow>();
        foreach (WorkRow row in list.Rows)
        {
            if (row.Action == WorkAction.Invite && texts.TryGetValue(row.Row, out string? text) && await NoteAsync(reads, row.Company, text, ct) is { } linked)
            {
                texts[row.Row] = linked;
                rows.Add(row with { Expected = JobLinkNote.Expected(linked) });
                continue;
            }
            rows.Add(row);
        }
        return list with { Rows = rows, Texts = texts };
    }

    private async Task<string?> NoteAsync(DayReads reads, string company, string text, CancellationToken ct)
    {
        string? url = reads.Rows
            .Where(row => row.Length >= 7 && string.Equals(row[3].Trim(), "Application", StringComparison.OrdinalIgnoreCase) && ApplicationCap.Same(row[2], company))
            .Select(row => JobLinkNote.PostingUrl(row[6])).LastOrDefault(found => found is not null);
        if (url is null || await postings.LiveTitleAsync(url, ct) is not { Length: > 0 } title)
        {
            return null;
        }
        string? note = JobLinkNote.Build(text, title, url);
        return note is not null && TextCheck.Problems(note, invite: true).Count == 0 && await prose.CleanAsync(note, ct) ? note : null;
    }
}
