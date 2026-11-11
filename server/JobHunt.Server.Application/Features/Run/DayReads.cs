namespace JobHunt.Server.Application.Features.Run;

/// <summary>Everything one morning's gates read, read once.</summary>
/// <param name="Day">The Central day.</param>
/// <param name="Rows">Tracker rows, master and unmerged spool, seven cells each.</param>
/// <param name="Limits">outreach-limits.md.</param>
/// <param name="Conventions">CONVENTIONS.md.</param>
/// <param name="Gate">The person and company gates.</param>
/// <param name="Queues">The open blocks of every queue.</param>
/// <param name="Holds">The people and companies linkedin-proof/FEED-HOLD.md holds for Steve, which no lane queues or sends to.</param>
/// <param name="Slot">The slot the lanes work for: the morning's, or pounce- and the caught role's key, which its codes are minted under.</param>
public sealed record DayReads(DateOnly Day, IReadOnlyList<string[]> Rows, string Limits, string Conventions, PersonGate Gate, IReadOnlyList<QueueBlock> Queues, FeedHolds Holds, string Slot = RunSlot.Hour);

/// <summary>Reads the shared files the gates need. A file that will not read stops the lanes that need it, loudly.</summary>
/// <param name="share">The share.</param>
public sealed class DayReader(IShare share)
{
    private const string Proof = "linkedin-proof";
    private const string Spool = "inbox/tracker-inbox";

    /// <summary>The morning's reads.</summary>
    /// <param name="day">The Central day.</param>
    /// <param name="ct">Stops the reads.</param>
    public async Task<DayReads> ReadAsync(DateOnly day, CancellationToken ct)
    {
        var rows = new List<string[]>(TrackerCsvText.Parse(await share.ReadAsync("Job_Hunt_Tracker.csv", ct)).Skip(1).Select(TrackerKinds.Seven));
        foreach (ShareFile file in await share.ListAsync(Spool, "*.csv", ct))
        {
            rows.AddRange(TrackerCsvText.Parse(await share.ReadAsync(file.Path, ct)).Select(TrackerKinds.Seven).Where(row => row.Length >= 7 && row[1] != "Person"));
        }
        string limits = await share.ReadAsync("outreach-limits.md", ct);
        string conventions = await share.ReadAsync("CONVENTIONS.md", ct);
        string rules = await share.ReadAsync("systemRules.md", ct);
        string responders = await share.ReadAsync("responders.md", ct);

        var files = await share.ListAsync(Proof, "QUEUE-*.md", ct);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ShareFile file in files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            texts[Path.GetFileName(file.Path)] = await share.ReadAsync(file.Path, ct);
        }
        var statuses = texts.Values.Where(text => QueueBlocks.StatusOf(text) is not null).ToList();
        var blocks = new List<QueueBlock>();
        var agencies = new List<string>();
        foreach ((string name, string text) in texts.Where(pair => QueueBlocks.StatusOf(pair.Value) is null))
        {
            blocks.AddRange(QueueBlocks.Open(name, text, statuses.Where(status => QueueBlocks.StatusOf(status) == name)));
            agencies.AddRange(QueueBlocks.Agencies(text));
        }
        var gate = new PersonGate(new GateSources(rows, limits, rules, responders, agencies), day);
        FeedHolds holds = await share.ExistsAsync(FeedLane.HoldFile, ct) ? FeedHold.Read(await share.ReadAsync(FeedLane.HoldFile, ct)) : FeedHolds.None;
        return new DayReads(day, rows, limits, conventions, gate, blocks, holds);
    }
}
