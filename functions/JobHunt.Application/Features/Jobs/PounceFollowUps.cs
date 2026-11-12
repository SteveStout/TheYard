using System.Globalization;
using System.Text;

namespace JobHunt.Application.Features.Jobs;

/// <summary>What the site says the tagged codes did, read from /api/admin/links and /api/admin/review.</summary>
public interface ISiteVisits
{
    /// <summary>One visit a code opened in the window, or null when the site keeps no opens or did not answer.</summary>
    Task<IReadOnlyList<CodeVisit>?> ReadAsync(CancellationToken ct);
}

/// <summary>What one follow-up pass did.</summary>
/// <param name="Codes">Codes minted on pounce notes in the last seven days.</param>
/// <param name="Opened">Of them, codes the site saw opened.</param>
/// <param name="Due">Of them, codes that reached rung 2 or were opened twice.</param>
/// <param name="Queued">Follow-ups queued this pass.</param>
/// <param name="Replied">Due codes whose person had already written back.</param>
/// <param name="Already">Due codes whose person already has a marker for the role: a follow-up queued or a reply seen.</param>
/// <param name="Blocked">Why the pass stopped before judging, or null.</param>
/// <param name="Unresolved">Due codes minted with no name whose send's row naming the person is not in the tracker yet; they wait.</param>
public sealed record FollowUpPass(int Codes, int Opened, int Due, int Queued, int Replied, int Already, string? Blocked = null, int Unresolved = 0);

/// <summary>
/// Reads the opens of every code minted on a pounce note and queues one follow-up item for each that earned it
/// (PounceFollowUp, FollowUpItem): the marker first, so a pass that dies half way can never queue a second follow-up
/// for the same person and role, then the item, renamed into place in one step, then one log line. A person the tracker
/// shows replied gets a marker and no item, so the log says it once. A code minted with no name stands for the person its
/// send's tracker row names (PounceFollowUp.Recipient), so two codes for one leader and role share one marker, and waits
/// for that row before it is judged. The mode "off" reads and writes nothing. The dry run reads the same and queues nothing: it
/// writes one report of the follow-ups it would queue and the minutes from each first open.
/// </summary>
/// <param name="store">The job-hunt share; link-tokens.csv and its spool are only read.</param>
/// <param name="site">The site's two cards.</param>
/// <param name="tracker">The tracker's rows.</param>
/// <param name="time">The clock.</param>
public sealed class PounceFollowUps(IMergeStore store, ISiteVisits site, ITrackerRows tracker, TimeProvider time)
{
    /// <summary>How far back a code is read: the window the site's cards are asked for.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    /// <summary>One pass: every due follow-up queued once, in the mode given.</summary>
    /// <param name="mode">Off, dry or live (PounceHandoff.Mode).</param>
    /// <param name="ct">Stops the pass.</param>
    public async Task<FollowUpPass> RunAsync(string mode, CancellationToken ct)
    {
        if (mode == PounceHandoff.Off)
        {
            return new FollowUpPass(0, 0, 0, 0, 0, 0, "switch_off");
        }
        var (pass, due) = await JudgeAsync(ct);
        if (pass.Blocked is not null || due.Count == 0)
        {
            return pass;
        }
        DateTimeOffset now = time.GetUtcNow();
        await store.EnsureFolderAsync(PounceFollowUp.Folder, ct);
        await store.EnsureFolderAsync(PounceHandoff.Ready, ct);
        int queued = 0, replied = 0, already = 0;
        var log = new StringBuilder();
        foreach (var (followUp, answered) in due)
        {
            string marker = PounceFollowUp.Marker(followUp.Code);
            if (await MarkedAsync(marker, followUp.Code, ct))
            {
                already++;
                continue;
            }
            string did;
            if (answered)
            {
                await store.WriteAsync(marker, TrackerCsvText.Encode($"{followUp.Code.Code} replied {now:O}\n"), ct);
                did = "replied, no follow-up";
                replied++;
            }
            else
            {
                string ready = FollowUpItem.ReadyPath(followUp);
                await store.WriteAsync(marker, TrackerCsvText.Encode($"{followUp.Code.Code} {ready} {now:O}\n"), ct);
                string temp = $"{PounceHandoff.Ready}/.pouncefollow-{followUp.Code.Code}.tmp";
                await store.WriteAsync(temp, TrackerCsvText.Encode(FollowUpItem.Item(followUp, mode, now)), ct);
                await store.ReplaceAsync(temp, ready, ct);
                did = $"queued {ready} ({(FollowUpItem.Sends(followUp, mode) ? "live" : "dry")})";
                queued++;
            }
            log.Append(PounceFollowUp.LogLine(CentralClock.Local(now), followUp, did, now)).Append('\n');
        }
        if (log.Length > 0)
        {
            await store.AppendAsync(PounceFollowUp.Log, TrackerCsvText.Encode(log.ToString()), ct);
        }
        return pass with { Queued = queued, Replied = replied, Already = already };
    }

    /// <summary>
    /// The dry run: the same reads and the same rules, and nothing queued and nothing marked. Writes one report under
    /// nightly/pounce/followup-dry/ holding each follow-up it would queue, word for word, with the whole minutes from the
    /// code's first open to the report. Answers the report's lines.
    /// </summary>
    /// <param name="ct">Stops the run.</param>
    public async Task<IReadOnlyList<string>> DryRunAsync(CancellationToken ct)
    {
        var (pass, due) = await JudgeAsync(ct);
        DateTimeOffset now = time.GetUtcNow();
        DateTime local = CentralClock.Local(now);
        var lines = new List<string> { string.Create(CultureInfo.InvariantCulture, $"Follow-up dry run {local:yyyy-MM-dd HH:mm:ss} Central: {pass.Codes} pounce codes, {pass.Opened} opened, {pass.Due} due.{(pass.Unresolved == 0 ? string.Empty : $" {pass.Unresolved} wait for the send's row that names the person.")}{(pass.Blocked is null ? string.Empty : $" Blocked: {pass.Blocked}.")}") };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (followUp, answered) in due)
        {
            string marker = PounceFollowUp.Marker(followUp.Code);
            bool already = !seen.Add(marker) || await MarkedAsync(marker, followUp.Code, ct);
            string did = already ? "already has a follow-up for this role" : answered ? "replied, no follow-up" : "dry follow-up, nothing queued";
            lines.Add(string.Empty);
            lines.Add(PounceFollowUp.LogLine(local, followUp, did, now));
            if (!already && !answered)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"Minutes from open to dry follow-up: {PounceFollowUp.Minutes(followUp.Visit, now)} ({PounceFollowUp.Words(followUp.Text)} words)"));
                lines.Add(followUp.Text.Replace("\n", " / ", StringComparison.Ordinal));
            }
        }
        await store.EnsureFolderAsync(PounceFollowUp.DryFolder, ct);
        await store.WriteAsync($"{PounceFollowUp.DryFolder}/{now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}Z.md", TrackerCsvText.Encode(string.Join("\n", lines) + "\n"), ct);
        return lines;
    }

    /// <summary>True when the person has a marker for the role, or the code has the one an earlier build wrote for it.</summary>
    private async Task<bool> MarkedAsync(string marker, PounceCode code, CancellationToken ct) =>
        await store.ExistsAsync(marker, ct) || await store.ExistsAsync(PounceFollowUp.LegacyMarker(code), ct);

    private async Task<IReadOnlyList<PounceCode>> CodesAsync(CancellationToken ct)
    {
        var files = new List<string>();
        if (await store.ExistsAsync(PounceFollowUp.Tokens, ct))
        {
            files.Add(PounceFollowUp.Tokens);
        }
        files.AddRange((await store.ListFilesAsync(PounceFollowUp.Spool, ct))
            .Where(f => f.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => $"{PounceFollowUp.Spool}/{f.Name}"));
        var codes = new Dictionary<string, PounceCode>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            foreach (PounceCode code in PounceFollowUp.Codes(TrackerCsvText.Decode(await store.ReadAsync(file, ct))))
            {
                codes.TryAdd(code.Code, code);
            }
        }
        return [.. codes.Values];
    }

    private async Task<(FollowUpPass Pass, List<(FollowUp FollowUp, bool Answered)> Due)> JudgeAsync(CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        var codes = (await CodesAsync(ct)).Where(c => c.Sent >= now - Window).ToList();
        if (codes.Count == 0)
        {
            return (new FollowUpPass(0, 0, 0, 0, 0, 0), []);
        }
        if (await site.ReadAsync(ct) is not { } visits)
        {
            return (new FollowUpPass(codes.Count, 0, 0, 0, 0, 0, "site_not_read"), []);
        }
        var byCode = visits.ToDictionary(v => v.Code, StringComparer.Ordinal);
        var opened = codes.Where(c => byCode.ContainsKey(c.Code)).ToList();
        var due = opened.Where(c => PounceFollowUp.Due(byCode[c.Code])).OrderBy(c => byCode[c.Code].FirstOpen).ToList();
        if (due.Count == 0)
        {
            return (new FollowUpPass(codes.Count, opened.Count, 0, 0, 0, 0), []);
        }
        var touches = await tracker.SinceAsync(due.Min(c => c.Sent), ct);
        var resolved = due.Select(c => PounceFollowUp.Recipient(touches, c)).OfType<PounceCode>().ToList();
        var found = resolved.Select(c =>
        {
            int rung = PounceFollowUp.Rung(byCode[c.Code]);
            return (new FollowUp(c, byCode[c.Code], rung, PounceFollowUp.Text(c, rung)), PounceFollowUp.Replied(touches, c));
        }).ToList();
        return (new FollowUpPass(codes.Count, opened.Count, due.Count, 0, 0, 0, Unresolved: due.Count - resolved.Count), found);
    }
}
