using System.Globalization;
using System.Text.Json;
using JobHunt.Application.Shared.Mail;
using JobHunt.Application.Shared.Runs;
using JobHunt.Domain.Features.Watch;
using JobHunt.Domain.Shared.Clock;
using JobHunt.Domain.Shared.Mail;
using JobHunt.Domain.Shared.Tracker;

namespace JobHunt.Application.Features.Watch;

/// <summary>What one tick read and wrote.</summary>
/// <param name="Results">Every test's reading, in the plan's order.</param>
/// <param name="Written">Whether WATCH.md was written (shadow and on write it; off writes nothing).</param>
/// <param name="Heals">The heal items the ladder opened (mode on only; shadow heals nothing).</param>
public sealed record WatchPassResult(IReadOnlyList<WatchResult> Results, bool Written, int Heals = 0);

/// <summary>
/// One watch tick (the year-end plan's 7a): each test reads its real source and is judged by <see cref="WatchJudge"/>,
/// then WATCH.md is written whole and the day's change log gets a line for each test whose word changed. The settings
/// come through <paramref name="setting"/>, the running process's own environment; a secret's value never leaves it:
/// only whether it is set is kept. A source this tick has no reader for yet is "not read", never a pass.
/// </summary>
/// <param name="store">The share.</param>
/// <param name="setting">A setting's value by name, as the running Function reads it.</param>
/// <param name="time">The clock.</param>
/// <param name="web">The public reads (the site's cards, GitHub); null leaves W9, W12 and the box's snapshot not read.</param>
/// <param name="mail">The card's sender, for the one NOT WORKING mail a failure a day to his own address (mode on); null sends nothing.</param>
public sealed class WatchPass(IMergeStore store, Func<string, string?> setting, TimeProvider time, IWatchWeb? web = null, IMailSender? mail = null)
{
    /// <summary>The site's two names.</summary>
    public const string Site = "https://theyard.stevenstout.biz";

    /// <summary>The Cosmos DB name.</summary>
    public const string Cosmos = "https://theyard-cosmos.stevenstout.biz";

    /// <summary>The repository's public API.</summary>
    public const string Repo = "https://api.github.com/repos/SteveStout/TheYard";

    private const string Switches = "dispatch/SWITCHES.md";
    private const string Heartbeat = "dispatch/HEARTBEAT";
    private const string Healed = "dispatch/HEALED.md";
    private const string Paused = "dispatch/paused.json";

    /// <summary>Runs one tick in a mode; off reads nothing and writes nothing.</summary>
    /// <param name="mode">The WATCH switch's mode.</param>
    /// <param name="ct">Stops the tick.</param>
    public async Task<WatchPassResult> RunAsync(WatchMode mode, CancellationToken ct)
    {
        if (mode == WatchMode.Off)
        {
            return new WatchPassResult([], false);
        }
        DateTimeOffset now = time.GetUtcNow();
        DateTime local = CentralClock.Local(now);
        var day = DateOnly.FromDateTime(local);
        string switches = await TextAsync(Switches, ct);
        var results = new List<WatchResult> { WatchJudge.Pounce(setting("POUNCE_RUN"), Recorded(switches, "POUNCE_RUN")) };
        results.AddRange(WatchJudge.Switches(Settings(switches), switches, day));
        NoEmailDays days = await NoEmailDaysAsync(ct);
        results.Add(days.Read && days.Listed(day)
            ? new WatchResult("W16", "no-email day", null, "a listed day; Gmail Sent is not read by this tick yet", "Gmail Sent today, against J:\\dispatch\\NO-EMAIL-DAYS.txt")
            : WatchJudge.NoEmailDay(days.Read ? false : null, [], string.Empty));
        using JsonDocument? box = web is null ? null : await web.GetAsync($"{Site}/api/admin/cloud/box?cb={now.ToUnixTimeSeconds()}", ct);
        TimeSpan? snapshot = box is not null && box.RootElement.TryGetProperty("read_age_seconds", out var age) && age.TryGetDouble(out double seconds) ? TimeSpan.FromSeconds(seconds) : null;
        var watchers = WatchJudge.Watchers(await AgeAsync(Heartbeat, now, ct), await AgeAsync(WatchTick.File, now, ct), snapshot, await PausedAsync(now, ct), local.Hour).ToList();
        if (box is null)
        {
            watchers[2] = watchers[2] with { Pass = null, Number = "not read" };
        }
        results.AddRange(watchers);
        if (web is not null)
        {
            results.Add(await SiteAsync(web, now, ct));
            results.AddRange(await BeatsAsync(web, box, now, ct));
            using var review = await web.GetAsync($"{Site}/api/admin/review?cb={now.ToUnixTimeSeconds()}", ct);
            results.Add(WatchBeats.Review(Number(review, "opened"), review is not null && review.RootElement.TryGetProperty("second", out var second) && second.TryGetProperty("second_action", out var reached) && reached.TryGetInt32(out int n) ? n : null));
        }
        results.AddRange(WatchBeats.Slots(local, await NewestAsync($"nightly/{day:yyyy-MM-dd}", "0400-reply-", "DONE.md", ct), await WrittenAsync($"nightly/{day:yyyy-MM-dd}/0600-card.md", ct),
            web is null ? null : await RunTodayAsync(web, "NetworkLogPass", day, ct), web is null ? null : await RunTodayAsync(web, "TodayCard", day, ct)));
        results.Add(WatchJudge.HealBudget(await TextAsync(Healed, ct), day));
        results.AddRange(WatchJudge.Canaries(await TextAsync("dispatch/canary.log", ct), local));
        results.Add(WatchJudge.Reached(await TextAsync("TODAY.md", ct)));
        var candidates = await store.StatAsync($"nightly/jobs-candidates-{day:yyyy-MM-dd}.tsv", ct);
        results.Add(WatchJudge.Supply(candidates is null ? null : CentralClock.Local(candidates.LastWritten), await ReadyAsync(day, ct), await ReadyAsync(day.AddDays(-1), ct), local));

        await store.EnsureFolderAsync("dispatch/watch", ct);
        string log = WatchTick.ChangeLog(day);
        string before = await TextAsync(log, ct);
        string changes = WatchTick.Changes(local, before, results);
        if (changes.Length > 0)
        {
            await store.AppendAsync(log, TrackerCsvText.Encode(changes), ct);
        }
        int heals = mode == WatchMode.On ? (await new WatchHeal(store, mail).RunAsync(results, before + changes, now, ct)).Items.Count : 0;
        // The dark-lane alarm tells in shadow too: a lane that sent nothing for three days is the card's first line (audit1124b F2).
        await new WatchDark(store).RunAsync(now, ct);
        await store.WriteAsync(MergePaths.Temp(WatchTick.File), TrackerCsvText.Encode(WatchTick.Render(local, mode, results)), ct);
        await store.ReplaceAsync(MergePaths.Temp(WatchTick.File), WatchTick.File, ct);
        return new WatchPassResult(results, true, heals);
    }

    /// <summary>W9: both names' commit, main's head and its CI run, two calls to GitHub.</summary>
    private static async Task<WatchResult> SiteAsync(IWatchWeb web, DateTimeOffset now, CancellationToken ct)
    {
        string? one = await CommitAsync(web, Site, now, ct), cosmos = await CommitAsync(web, Cosmos, now, ct);
        using var main = await web.GetAsync($"{Repo}/commits/main", ct);
        string? sha = main?.RootElement.TryGetProperty("sha", out var s) == true ? s.GetString() : null;
        DateTimeOffset? at = main is not null && main.RootElement.TryGetProperty("commit", out var c) && c.TryGetProperty("committer", out var who)
            && who.TryGetProperty("date", out var d) && DateTimeOffset.TryParse(d.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var when) ? when : null;
        string? ci = null;
        if (sha is not null)
        {
            using var runs = await web.GetAsync($"{Repo}/actions/runs?head_sha={sha}&per_page=30", ct);
            if (runs is not null && runs.RootElement.TryGetProperty("workflow_runs", out var list))
            {
                // ci.yml names its run "CI <the commit it tested>" (its run-name); a run named for another commit is not this one's.
                ci = list.EnumerateArray().Where(r => r.GetProperty("name").GetString() is { } n && (n == "CI" || n == $"CI {sha}") && r.GetProperty("status").GetString() == "completed")
                    .Select(r => r.GetProperty("conclusion").GetString()).FirstOrDefault();
            }
        }
        return WatchBeats.Site(one, cosmos, sha, at, ci, now);
    }

    private static async Task<string?> CommitAsync(IWatchWeb web, string site, DateTimeOffset now, CancellationToken ct)
    {
        using var version = await web.GetAsync($"{site}/api/version?cb={now.ToUnixTimeSeconds()}", ct);
        return version?.RootElement.TryGetProperty("commit", out var c) == true ? c.GetString() : null;
    }

    /// <summary>
    /// W12: the nightly card's firing and merger rows, each Function process's newest listed run, and each unit on the
    /// box card. A process the card's route does not know answers not read.
    /// </summary>
    private static async Task<IReadOnlyList<WatchResult>> BeatsAsync(IWatchWeb web, JsonDocument? box, DateTimeOffset now, CancellationToken ct)
    {
        var beats = new List<WatchResult>();
        using (var card = await web.GetAsync($"{Site}/api/admin/cloud/nightly?cb={now.ToUnixTimeSeconds()}", ct))
        {
            foreach (var row in Rows(card).Where(r => Text(r, "key") is "firing" or "merger"))
            {
                beats.Add(WatchBeats.CardRow(Text(row, "key")!, Text(row, "tone") ?? "", Text(row, "answer") ?? ""));
            }
        }
        foreach (string process in RunFloors.All.Select(f => f.Process).Where(p => !p.Contains(' ', StringComparison.Ordinal)))
        {
            using var runs = await web.GetAsync($"{Site}/api/admin/cloud/nightly/runs/{process}", ct);
            var newest = Rows(runs).OrderByDescending(r => r.TryGetProperty("at_ms", out var ms) && ms.TryGetInt64(out long t) ? t : 0).FirstOrDefault();
            beats.Add(runs is null ? new WatchResult("W12", process, null, "not read", $"/api/admin/cloud/nightly/runs/{process}")
                : WatchBeats.Process(process, newest.ValueKind == JsonValueKind.Object ? Text(newest, "outcome") : null, newest.ValueKind == JsonValueKind.Object ? Text(newest, "when") : null));
        }
        var services = Rows(box).FirstOrDefault(r => Text(r, "key") == "services");
        if (services.ValueKind == JsonValueKind.Object && services.TryGetProperty("table", out var table) && table.TryGetProperty("rows", out var units))
        {
            foreach (var unit in units.EnumerateArray().Where(u => u.GetArrayLength() >= 2))
            {
                beats.Add(WatchBeats.Unit(Text(unit[0], "text") ?? "", Text(unit[1], "text") ?? ""));
            }
        }
        return beats;
    }

    /// <summary>
    /// When a process ran today, from its run history's newest row of today (the card names a day as "Fri 20 Nov"): the
    /// run line is the slot's own record, where a file's write time is not (lanes append to TODAY.md all day).
    /// </summary>
    private static async Task<DateTime?> RunTodayAsync(IWatchWeb web, string process, DateOnly day, CancellationToken ct)
    {
        using var runs = await web.GetAsync($"{Site}/api/admin/cloud/nightly/runs/{process}", ct);
        string today = day.ToString("ddd d MMM", CultureInfo.InvariantCulture);
        var row = Rows(runs).FirstOrDefault(r => Text(r, "day") == today);
        return row.ValueKind == JsonValueKind.Object && TimeOnly.TryParse(Text(row, "when"), CultureInfo.InvariantCulture, out var at) ? day.ToDateTime(at) : null;
    }

    private async Task<DateTime?> WrittenAsync(string path, CancellationToken ct) =>
        await store.StatAsync(path, ct) is { } entry ? CentralClock.Local(entry.LastWritten) : null;

    /// <summary>When the newest file of that name was written under the folder's subfolders starting with a prefix.</summary>
    private async Task<DateTime?> NewestAsync(string folder, string prefix, string file, CancellationToken ct)
    {
        DateTime? newest = null;
        foreach (string sub in (await store.ListFoldersAsync(folder, ct)).Where(f => f.StartsWith(prefix, StringComparison.Ordinal)))
        {
            if (await WrittenAsync($"{folder}/{sub}/{file}", ct) is { } at && (newest is null || at > newest))
            {
                newest = at;
            }
        }
        return newest;
    }

    private static int? Number(JsonDocument? json, string name) =>
        json is not null && json.RootElement.TryGetProperty(name, out var value) && value.TryGetInt32(out int n) ? n : null;

    private static IEnumerable<JsonElement> Rows(JsonDocument? json) =>
        json is not null && json.RootElement.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array ? rows.EnumerateArray().ToList() : [];

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>A setting's value cell in SWITCHES.md, or null when it has no row.</summary>
    public static string? Recorded(string switches, string name) =>
        (switches ?? string.Empty).Split('\n').Select(l => l.Split('|', StringSplitOptions.TrimEntries)).FirstOrDefault(c => c.Length > 3 && c[1] == name)?[2];

    /// <summary>The settings SWITCHES.md names, as the running Function reads them: a switch's value, and for a secret only "set".</summary>
    private Dictionary<string, string?> Settings(string switches)
    {
        var live = new Dictionary<string, string?>(StringComparer.Ordinal);
        bool secrets = false;
        foreach (string line in (switches ?? string.Empty).Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                secrets = line.StartsWith("## Secrets", StringComparison.Ordinal);
                continue;
            }
            string[] cells = line.Split('|', StringSplitOptions.TrimEntries);
            if (cells.Length > 3 && cells[1].Length > 0 && cells[1].All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                string? value = setting(cells[1]);
                live[cells[1]] = secrets ? (string.IsNullOrEmpty(value) ? null : "set") : value;
            }
        }
        return live;
    }

    private async Task<NoEmailDays> NoEmailDaysAsync(CancellationToken ct)
    {
        try
        {
            return await store.ExistsAsync(NoEmailDays.File, ct) ? NoEmailDays.Parse(await TextAsync(NoEmailDays.File, ct)) : NoEmailDays.None;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return NoEmailDays.None;
        }
    }

    private async Task<int?> ReadyAsync(DateOnly day, CancellationToken ct)
    {
        string path = $"JOBS-{day:yyyy-MM-dd}.md";
        return await store.ExistsAsync(path, ct) ? WatchJudge.ReadyRows(await TextAsync(path, ct)) : null;
    }

    private async Task<TimeSpan?> AgeAsync(string path, DateTimeOffset now, CancellationToken ct) =>
        await store.StatAsync(path, ct) is { } entry ? now - entry.LastWritten : null;

    /// <summary>How long the dispatcher has been paused, from paused.json's write time, while its until is still ahead.</summary>
    private async Task<TimeSpan?> PausedAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (await store.StatAsync(Paused, ct) is not { } entry)
        {
            return null;
        }
        try
        {
            using var json = JsonDocument.Parse(await TextAsync(Paused, ct));
            return json.RootElement.TryGetProperty("until", out var until) && DateTimeOffset.TryParse(until.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) && at > now
                ? now - entry.LastWritten
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string> TextAsync(string path, CancellationToken ct) =>
        await store.ExistsAsync(path, ct) ? TrackerCsvText.Decode(await store.ReadAsync(path, ct)) : string.Empty;
}
