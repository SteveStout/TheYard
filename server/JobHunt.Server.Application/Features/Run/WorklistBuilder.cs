using System.Text.RegularExpressions;

namespace JobHunt.Server.Application.Features.Run;

/// <summary>A candidate with its text, before the cut.</summary>
/// <param name="Person">The person.</param>
/// <param name="Company">The company.</param>
/// <param name="Action">The action.</param>
/// <param name="Text">The drafted text, empty for an application.</param>
/// <param name="Url">The profile or posting URL.</param>
/// <param name="Source">Where it came from.</param>
/// <param name="Role">An application's posting title.</param>
public sealed record Candidate(string Person, string Company, WorkAction Action, string Text, string Url, string Source, string Role = "");

/// <summary>A lane's worklist: the rows, their texts, and what the gates left out.</summary>
/// <param name="Rows">The rows, cut to the day's room.</param>
/// <param name="Texts">Each row's text by its row number.</param>
/// <param name="Dropped">What was left out, with the gate's reason.</param>
/// <param name="Blockers">Postings behind an account wall, for the day's blockers list.</param>
public sealed record Worklist(IReadOnlyList<WorkRow> Rows, IReadOnlyDictionary<int, string> Texts, IReadOnlyList<Dropped> Dropped, IReadOnlyList<Posting> Blockers);

/// <summary>
/// Builds a lane's worklist from what is already drafted. Every text runs through the short checks and the proofread;
/// one that fails is dropped with a line and never fixed. The cut to the day's room happens before any code is minted,
/// so a row the lane will not reach spends nothing. A dry run mints nothing and carries a placeholder code. A text that
/// speaks of an application the tracker says is over waits for a person, and every application gets its own resume build
/// with the posting's title as its headline, a dry run's too, so a morning's first build is not its first proof.
/// </summary>
/// <param name="share">The share.</param>
/// <param name="minter">make_ref.py.</param>
/// <param name="prose">check_aiisms.py.</param>
/// <param name="resumes">build_tailored_resume.py.</param>
/// <param name="config">The settings.</param>
public sealed partial class WorklistBuilder(IShare share, ICodeMinter minter, IProseCheck prose, IResumeBuilder resumes, ServerConfig config)
{
    private const string DryCode = "dryrun";
    private const int NewestQueueDays = 4;

    /// <summary>The invite worklist.</summary>
    /// <param name="reads">The morning's reads.</param>
    /// <param name="room">Invites left today.</param>
    /// <param name="dry">True for a dry run.</param>
    /// <param name="ct">Stops the build.</param>
    public async Task<Worklist> InvitesAsync(DayReads reads, int room, bool dry, CancellationToken ct)
    {
        var candidates = reads.Queues.Where(block => block.Action == WorkAction.Invite)
            .Select(block => new Candidate(block.Person, block.Company, WorkAction.Invite, block.Text, string.Empty, block.Source));
        return await FinishAsync(candidates, reads, candidate => reads.Gate.Invite(candidate.Person, candidate.Company), room, dry, ct);
    }

    /// <summary>The message worklist.</summary>
    /// <param name="reads">The morning's reads.</param>
    /// <param name="room">Messages left today.</param>
    /// <param name="dry">True for a dry run.</param>
    /// <param name="ct">Stops the build.</param>
    public async Task<Worklist> MessagesAsync(DayReads reads, int room, bool dry, CancellationToken ct)
    {
        var dropped = new List<Dropped>();
        var candidates = reads.Queues.Where(block => block.Action == WorkAction.Message)
            .Select(block => new Candidate(block.Person, block.Company, WorkAction.Message, block.Text, Profile(reads, block.Person, block.Company), block.Source))
            .ToList();
        if (await NewestReplyQueueAsync(reads.Day, ct) is { } newest)
        {
            string queue = await share.ReadAsync(newest, ct);
            string? drafts = ReplyQueue.DraftFolder(queue);
            foreach (ReplyItem item in ReplyQueue.Items(queue))
            {
                string name = item.Person.Length > 0 ? item.Person : item.Company;
                string company = item.Company.Length > 0 ? item.Company : reads.Gate.CompanyOf(item.Person);
                string path = drafts is null || company.Length == 0 ? string.Empty : $"{drafts}/{ReplyQueue.DraftName(company)}";
                if (path.Length == 0 || !await share.ExistsAsync(path, ct))
                {
                    dropped.Add(new Dropped(name, "no drafted text"));
                    continue;
                }
                string body = ReplyQueue.Body(await share.ReadAsync(path, ct));
                string first = ReplyQueue.Greeted(body);
                if (item.Person.Length > 0 && !item.Person.StartsWith(first + " ", StringComparison.OrdinalIgnoreCase))
                {
                    dropped.Add(new Dropped(name, $"the draft greets {first}"));
                    continue;
                }
                string? person = FullName(reads, first, company);
                if (person is null)
                {
                    dropped.Add(new Dropped(name, $"the draft greets {first}, and no tracker row at {company} gives a full name to check Rule 25 by"));
                    continue;
                }
                candidates.Add(new Candidate(person, company, WorkAction.Message, body, Profile(reads, person, company), $"{item.Source}, {path}"));
            }
        }
        Worklist list = await FinishAsync(candidates, reads, candidate => reads.Gate.Message(candidate.Person, candidate.Company), room, dry, ct);
        return list with { Dropped = [.. dropped, .. list.Dropped] };
    }

    /// <summary>The apply worklist.</summary>
    /// <param name="reads">The morning's reads.</param>
    /// <param name="linkedInClosed">Why LinkedIn is closed today, or null; its postings then stay off the list.</param>
    /// <param name="dry">True for a dry run.</param>
    /// <param name="ct">Stops the build.</param>
    public async Task<Worklist> ApplyAsync(DayReads reads, string? linkedInClosed, bool dry, CancellationToken ct)
    {
        var postings = new List<Posting>();
        foreach (string pattern in new[] { "APPLY-SWEEP-*.md", "priority-jobs-*.md" })
        {
            foreach (ShareFile file in (await share.ListAsync(string.Empty, pattern, ct)).Where(file => reads.Day.DayNumber - DateOnly.FromDateTime(file.Changed.UtcDateTime).DayNumber <= ApplyBoards.NewestDays))
            {
                postings.AddRange(ApplyBoards.Confirmed(file.Path, await share.ReadAsync(file.Path, ct)));
            }
        }
        postings.AddRange(ApplyBoards.Scored(await share.ReadAsync("leads/scored.tsv", ct), await share.ReadAsync("nightly/LEADS.md", ct), reads.Day));

        var dropped = new List<Dropped>();
        var blockers = new List<Posting>();
        var candidates = new List<Candidate>();
        foreach (Posting posting in postings.DistinctBy(posting => posting.Link))
        {
            string what = $"{posting.Company}, {posting.Role}";
            if (reads.Gate.Apply(posting.Company, posting.Link, posting.Role) is { } reason)
            {
                dropped.Add(new Dropped(what, reason));
            }
            else if (linkedInClosed is not null && ApplyBoards.OnLinkedIn(posting))
            {
                dropped.Add(new Dropped(what, $"on LinkedIn, and LinkedIn is closed today: {linkedInClosed}"));
            }
            else if (!ApplyBoards.Reachable(posting))
            {
                blockers.Add(posting);
                dropped.Add(new Dropped(what, "skipped: account wall or no form this lane may use"));
            }
            else if (candidates.Any(candidate => ApplicationCap.Same(candidate.Company, posting.Company)))
            {
                dropped.Add(new Dropped(what, "one application a company a morning"));
            }
            else
            {
                candidates.Add(new Candidate(string.Empty, posting.Company, WorkAction.Apply, string.Empty, posting.Link, $"{posting.Source}, {posting.Role}", posting.Role));
            }
        }
        int room = dry ? config.DryRows : config.MostApplications;
        Worklist list = await FinishAsync(candidates, reads, _ => null, room, dry, ct);
        return list with { Dropped = [.. dropped, .. list.Dropped], Blockers = blockers };
    }

    private async Task<string?> NewestReplyQueueAsync(DateOnly day, CancellationToken ct)
    {
        for (int back = 0; back < NewestQueueDays; back++)
        {
            string folder = RunSlot.DayFolder(day.AddDays(-back));
            foreach (string run in (await share.FoldersAsync(folder, "0400-reply-*", ct)).OrderDescending(StringComparer.Ordinal))
            {
                if (await share.ExistsAsync($"{run}/linkedin-queue.md", ct))
                {
                    return $"{run}/linkedin-queue.md";
                }
            }
        }
        return null;
    }

    private async Task<Worklist> FinishAsync(IEnumerable<Candidate> candidates, DayReads reads, Func<Candidate, string?> gate, int room, bool dry, CancellationToken ct)
    {
        var dropped = new List<Dropped>();
        var kept = new List<Candidate>();
        foreach (Candidate candidate in candidates)
        {
            string what = candidate.Person.Length > 0 ? $"{candidate.Person}, {candidate.Company}" : candidate.Company;
            string? reason = gate(candidate) ?? await TextReasonAsync(candidate, reads, ct);
            if (reason is not null)
            {
                dropped.Add(new Dropped(what, reason));
            }
            else
            {
                kept.Add(candidate);
            }
        }
        int limit = dry ? Math.Min(room, config.DryRows) : room;
        foreach (Candidate over in kept.Skip(Math.Max(0, limit)))
        {
            dropped.Add(new Dropped($"{over.Person} {over.Company}".Trim(), "over the day's room, stays queued"));
        }
        var rows = new List<WorkRow>();
        var texts = new Dictionary<int, string>();
        foreach (Candidate candidate in kept.Take(Math.Max(0, limit)))
        {
            string code = TextCheck.Code(candidate.Text);
            if (code.Length == 0 && candidate.Action != WorkAction.Apply)
            {
                string who = candidate.Person.Length > 0 ? candidate.Person : $"Leader at {candidate.Company}, name found at send";
                string? minted = dry ? DryCode : await minter.MintAsync(who, candidate.Company, ct);
                if (minted is null)
                {
                    dropped.Add(new Dropped(candidate.Company, "make_ref.py failed, no code, stays queued"));
                    continue;
                }
                code = minted;
            }
            int number = rows.Count + 1;
            ResumeBuild? resume = null;
            if (candidate.Action == WorkAction.Apply)
            {
                string folder = share.Full($"{RunSlot.RunFolder(reads.Day, Lane.Apply, config.For(Lane.Apply).Model, dry)}/resumes/{number:00}");
                resume = await resumes.BuildAsync(candidate.Role, candidate.Company, folder, ct);
                if (resume.Failure is { } failure)
                {
                    dropped.Add(new Dropped($"{candidate.Company}, {candidate.Role}", $"the resume did not build, stays queued: {failure}"));
                    continue;
                }
            }
            string text = candidate.Text.Replace("[code]", code, StringComparison.Ordinal);
            string file = candidate.Action == WorkAction.Apply ? string.Empty : $"texts/{number:00}.txt";
            string avoid = candidate.Person.Length == 0 && candidate.Action == WorkAction.Invite ? string.Join("; ", reads.Gate.KnownAt(candidate.Company)) : string.Empty;
            rows.Add(new WorkRow(number, candidate.Person, candidate.Company, candidate.Action, file, code, candidate.Url, candidate.Source, avoid, candidate.Role, resume));
            texts[number] = text;
        }
        return new Worklist(rows, texts, dropped, []);
    }

    private async Task<string?> TextReasonAsync(Candidate candidate, DayReads reads, CancellationToken ct)
    {
        if (candidate.Action == WorkAction.Apply)
        {
            return null;
        }
        if (reads.Rows.Any(row => row.Length >= 7 && row[6].Contains($"Source {candidate.Source}", StringComparison.Ordinal)))
        {
            return "this block already went out";
        }
        string code = TextCheck.Code(candidate.Text);
        if (code.Length > 0 && reads.Rows.Any(row => row.Length >= 7 && row[6].Contains(code, StringComparison.Ordinal)))
        {
            return $"code {code} already on a tracker row";
        }
        var problems = TextCheck.Problems(candidate.Text, candidate.Action == WorkAction.Invite);
        if (problems.Count > 0)
        {
            return "text failed: " + string.Join(", ", problems);
        }
        if (reads.Gate.ClosedReq(candidate.Company, candidate.Text) is { } closed)
        {
            return closed;
        }
        return await prose.CleanAsync(candidate.Text, ct) ? null : "text failed check_aiisms.py";
    }

    private static string? FullName(DayReads reads, string first, string company) =>
        first.Length == 0
            ? null
            : reads.Rows.Where(row => row.Length >= 7 && ApplicationCap.Same(row[2], company) && row[1].StartsWith(first + " ", StringComparison.OrdinalIgnoreCase))
                .Select(row => row[1].Trim()).FirstOrDefault();

    private static string Profile(DayReads reads, string person, string company) =>
        reads.Rows.Where(row => row.Length >= 7 && string.Equals(row[1].Trim(), person, StringComparison.OrdinalIgnoreCase) && ApplicationCap.Same(row[2], company))
            .Select(row => ProfileLink().Match(row[5] + " " + row[6]))
            .Where(match => match.Success).Select(match => "https://www." + match.Value).LastOrDefault() ?? string.Empty;

    [GeneratedRegex(@"linkedin\.com/in/[\w%-]+")]
    private static partial Regex ProfileLink();
}
