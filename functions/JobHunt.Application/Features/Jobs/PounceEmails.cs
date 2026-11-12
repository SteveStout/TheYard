using System.Globalization;
using System.Text;

namespace JobHunt.Application.Features.Jobs;

/// <summary>An address a finder gave for a person, with the public page it cites.</summary>
/// <param name="Address">The address.</param>
/// <param name="SourceUrl">The page it was read from.</param>
public sealed record LeaderAddress(string Address, string SourceUrl);

/// <summary>Looks a person's address up on a company's domain; built only when the finder's key is set.</summary>
public interface ILeaderFinder
{
    /// <summary>The finder's name, which its month count is kept under.</summary>
    string Name { get; }

    /// <summary>An address the finder scored high enough to keep, with its page, or null.</summary>
    Task<LeaderAddress?> FindAsync(string fullName, string domain, CancellationToken ct);
}

/// <summary>What one email pass did.</summary>
/// <param name="Roles">Hand-offs from the last day.</param>
/// <param name="Leaders">Of them, unsettled roles whose feed named a leader.</param>
/// <param name="Asked">Lookups spent this pass.</param>
/// <param name="Kept">Addresses kept, in shadow.</param>
/// <param name="Refused">Roles settled with no address: refused, no domain, or a company emailed this month.</param>
/// <param name="Already">Roles settled by an earlier pass.</param>
/// <param name="Waiting">Roles with no leader named yet, or a leader the spent month could not look up.</param>
/// <param name="Blocked">Why the pass did nothing, or null.</param>
public sealed record EmailPass(int Roles, int Leaders, int Asked, int Kept, int Refused, int Already, int Waiting, string? Blocked = null);

/// <summary>
/// The email leg of each caught role, in shadow (PounceEmail): for every hand-off of the last day, the leader the slot
/// run's feed named (the live queue in linkedin-proof/, else the dry feed's copy in its run folder) is looked up once
/// with the finder on the company's domain, under the campaign's own monthly count, and the answer is kept only when it
/// carries the leader's own name. A company that had a real campaign email in the last 30 days gets no lookup. The
/// marker is written once the role is settled, then one log line. Nothing here can send: it is handed no mail port.
/// The mode "off" reads and writes nothing.
/// </summary>
/// <param name="store">The job-hunt share.</param>
/// <param name="finder">The finder, or null when its key is not set.</param>
/// <param name="sends">The campaign's real sends.</param>
/// <param name="time">The clock.</param>
/// <param name="monthCap">The finder's lookups a month.</param>
public sealed class PounceEmails(IMergeStore store, ILeaderFinder? finder, IPounceReader sends, TimeProvider time, int monthCap)
{
    /// <summary>One pass over the last day's hand-offs.</summary>
    /// <param name="mode">Off, dry or live (PounceHandoff.Mode); the email stays in shadow in all three.</param>
    /// <param name="ct">Stops the pass.</param>
    public async Task<EmailPass> RunAsync(string mode, CancellationToken ct)
    {
        if (mode == PounceHandoff.Off)
        {
            return new EmailPass(0, 0, 0, 0, 0, 0, 0, "switch_off");
        }
        DateTimeOffset now = time.GetUtcNow();
        DateTime local = CentralClock.Local(now);
        var roles = await RolesAsync(now, ct);
        if (roles.Count == 0)
        {
            return new EmailPass(0, 0, 0, 0, 0, 0, 0);
        }
        string count = PounceEmail.CountPath(finder?.Name ?? "none", local);
        int used = await UsedAsync(count, ct);
        IReadOnlyList<EmailTouch>? emailed = null;
        int leaders = 0, asked = 0, kept = 0, refused = 0, already = 0, waiting = 0;
        var log = new LogWriter(store);
        foreach (EmailRole role in roles)
        {
            string marker = PounceEmail.Marker(role.Key);
            if (await store.ExistsAsync(marker, ct))
            {
                already++;
                await log.RecoverAsync(TrackerCsvText.Decode(await store.ReadAsync(marker, ct)), ct);
                continue;
            }
            if (await LeaderAsync(role, ct) is not { } leader)
            {
                waiting++;
                continue;
            }
            leaders++;
            emailed ??= await sends.EmailsSinceAsync(now.AddDays(-PounceEmail.CompanyDays), ct);
            string? reason = role.CompanyKey is null ? "no company domain known"
                : emailed.Any(e => ApplicationCap.Same(e.Company, role.Company) || CompanyKey.Of(e.To) == role.CompanyKey) ? "one email a company a month" : null;
            if (reason is null)
            {
                if (finder is null || used >= monthCap)
                {
                    waiting++;
                    continue;
                }
                used++;
                asked++;
                await store.EnsureFolderAsync("campaign/finder", ct);
                await store.WriteAsync(count, Encoding.UTF8.GetBytes(used.ToString(CultureInfo.InvariantCulture)), ct);
                var answer = await finder.FindAsync(leader, role.CompanyKey!, ct);
                reason = answer is null ? "the finder kept no address" : PounceEmail.Refuse(answer.Address, answer.SourceUrl, leader, role.CompanyKey!);
                if (reason is null)
                {
                    await SettleAsync(marker, PounceEmail.Kept(role, leader, answer!.Address.Trim(), answer.SourceUrl, now), PounceEmail.LogLine(local, role, leader, $"shadow, kept {answer.Address.Trim()}"), log, ct);
                    kept++;
                    continue;
                }
            }
            await SettleAsync(marker, PounceEmail.Settled(role, leader, reason, now), PounceEmail.LogLine(local, role, leader, $"refused: {reason}"), log, ct);
            refused++;
        }
        return new EmailPass(roles.Count, leaders, asked, kept, refused, already, waiting);
    }

    private async Task<List<EmailRole>> RolesAsync(DateTimeOffset now, CancellationToken ct)
    {
        var roles = new List<EmailRole>();
        foreach (MergeEntry file in (await store.ListFilesAsync(PounceHandoff.Folder, ct)).Where(f => f.Name.EndsWith(".json", StringComparison.Ordinal)).OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            if (PounceEmail.Read(TrackerCsvText.Decode(await store.ReadAsync($"{PounceHandoff.Folder}/{file.Name}", ct))) is { } role
                && now - role.PassedAt <= PounceEmail.Window && role.PassedAt <= now)
            {
                roles.Add(role);
            }
        }
        return roles;
    }

    /// <summary>The leader the role's queue names: the live queue first, else the dry feed's copy, the newest day first.</summary>
    private async Task<string?> LeaderAsync(EmailRole role, CancellationToken ct)
    {
        string suffix = $"-{role.Key.ToString(CultureInfo.InvariantCulture)}.md";
        var paths = (await store.ListFilesAsync("linkedin-proof", ct))
            .Where(f => f.Name.StartsWith("QUEUE-pounce-", StringComparison.Ordinal) && f.Name.EndsWith(suffix, StringComparison.Ordinal))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal).Select(f => $"linkedin-proof/{f.Name}").ToList();
        DateOnly first = DateOnly.FromDateTime(CentralClock.Local(role.PassedAt));
        for (DateOnly day = DateOnly.FromDateTime(CentralClock.Local(time.GetUtcNow())); day >= first; day = day.AddDays(-1))
        {
            string folder = $"nightly/{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
            foreach (string run in (await store.ListFoldersAsync(folder, ct)).Where(n => n.StartsWith($"pounce-{role.Key.ToString(CultureInfo.InvariantCulture)}-feed-", StringComparison.Ordinal)))
            {
                paths.AddRange((await store.ListFilesAsync($"{folder}/{run}", ct)).Where(f => f.Name.StartsWith("QUEUE-pounce-", StringComparison.Ordinal) && f.Name.EndsWith(suffix, StringComparison.Ordinal)).Select(f => $"{folder}/{run}/{f.Name}"));
            }
        }
        foreach (string path in paths)
        {
            if (PounceEmail.Leader(TrackerCsvText.Decode(await store.ReadAsync(path, ct))) is { } leader)
            {
                return leader;
            }
        }
        return null;
    }

    /// <summary>
    /// The marker first, carrying the role's log line, then that line on the log at once: a later role or a log write that
    /// fails costs no settled role its line, because the next pass finds the marker and writes the line it carries.
    /// </summary>
    private async Task SettleAsync(string marker, string text, string line, LogWriter log, CancellationToken ct)
    {
        await store.EnsureFolderAsync(PounceEmail.Folder, ct);
        await store.WriteAsync(marker, TrackerCsvText.Encode(text + PounceEmail.LogField + line + "\n"), ct);
        await log.AppendAsync(line, ct);
    }

    /// <summary>The log, read at most once a pass, so a line already on it is never written a second time.</summary>
    private sealed class LogWriter(IMergeStore store)
    {
        private string? _text;

        /// <summary>Writes the line a settled role's marker carries when the log does not hold it yet.</summary>
        public async Task RecoverAsync(string marker, CancellationToken ct)
        {
            if (PounceEmail.MarkerLog(marker) is { } line && !(await TextAsync(ct)).Contains(line + "\n", StringComparison.Ordinal))
            {
                await AppendAsync(line, ct);
            }
        }

        /// <summary>Appends one line and keeps it in the copy this pass reads.</summary>
        public async Task AppendAsync(string line, CancellationToken ct)
        {
            string text = await TextAsync(ct);
            await store.AppendAsync(PounceEmail.Log, TrackerCsvText.Encode(line + "\n"), ct);
            _text = text + line + "\n";
        }

        private async Task<string> TextAsync(CancellationToken ct) =>
            _text ??= await store.ExistsAsync(PounceEmail.Log, ct) ? TrackerCsvText.Decode(await store.ReadAsync(PounceEmail.Log, ct)) : string.Empty;
    }

    private async Task<int> UsedAsync(string path, CancellationToken ct) =>
        await store.ExistsAsync(path, ct) && int.TryParse(Encoding.UTF8.GetString(await store.ReadAsync(path, ct)).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0;
}
