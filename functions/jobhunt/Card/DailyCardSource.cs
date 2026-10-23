using System.Globalization;
using System.Text.Json;
using JobHunt.Functions.Merge;

namespace JobHunt.Functions.Card;

/// <summary>
/// Reads what the card says: the share's files (the newest APPLY-BLOCKERS file, the merger's and the
/// appends' logs, the rejected spools, the Sheet mirror's and the ATS sweep's last lines, the tracker)
/// and two public answers (TheYard's newest CI run on main and both live names' version). A read that
/// fails costs its own line, which says so; the card is still written.
/// </summary>
public sealed class DailyCardSource(IMergeStore store, IHttpClientFactory http, TimeProvider time)
{
    /// <summary>Every fact the card draws.</summary>
    public async Task<CardFacts> ReadAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var local = CentralClock.Local(now);
        var blockers = new List<string>();
        string? newest = DailyCard.Newest((await store.ListFilesAsync("", ct)).Select(f => f.Name), DateOnly.FromDateTime(local));
        if (newest is not null)
        {
            blockers.AddRange(DailyCard.Blockers(newest, await TextAsync(newest, ct)));
        }
        var held = new List<string>();
        string sheet = await LastLineAsync("sheet-mirror/mirror.log", ct);
        if (sheet.Contains("STOPPED", StringComparison.Ordinal))
        {
            held.Add("The Sheet waits: " + sheet[(sheet.IndexOf("STOPPED", StringComparison.Ordinal) + 8)..].Trim() + " (sheet-mirror/mirror.log)");
        }
        var week = await WeekAsync(now, ct);
        held.AddRange(week.Where(line => line.StartsWith("STOP", StringComparison.Ordinal)));
        var master = TrackerCsvText.Parse(await TextAsync(MergePaths.Master, ct)).Where(r => r.Length >= 7 && int.TryParse(r[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _)).ToList();
        var capped = Leads.ApplicationCap.Held(master, Leads.ApplicationCap.DoNotApplyList(await TextAsync("systemRules.md", ct)), DateOnly.FromDateTime(local));
        if (capped.Count > 0)
        {
            held.Add(CapLine(capped));
        }
        var pipeline = new List<string>
        {
            $"Tracker: {master.Count:N0} rows, last Count {(master.Count == 0 ? "not read" : master.Max(r => int.Parse(r[0].Trim(), CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture))} ({MergePaths.Master})",
            "Merger: " + Age(await LastLineAsync(MergePaths.Log, ct), local) + $" ({MergePaths.Log})",
            "Appends: " + Age(await LastLineAsync("append-inbox/merge.log", ct), local) + " (append-inbox/merge.log)",
            $"Rejected spool files waiting: {await CountAsync("tracker-inbox/rejected", ct) + await CountAsync("inbox/tracker-inbox/rejected", ct)} (tracker-inbox/rejected and inbox/tracker-inbox/rejected)",
            "Sheet mirror: " + Or(sheet, "no pass yet") + " (sheet-mirror/mirror.log)",
            "ATS sweep: " + Or(await LastLineAsync("ats-sweep/sweep.log", ct), "no pass yet") + " (ats-sweep/sweep.log)",
        };
        var yard = new List<string> { await CiAsync(ct) };
        foreach (string host in new[] { "theyard.stevenstout.biz", "theyard-cosmos.stevenstout.biz" })
        {
            yard.Add(await VersionAsync(host, ct));
        }
        return new CardFacts(local, blockers, held, week, pipeline, yard);
    }

    /// <summary>
    /// The card's one line for RULE 50: how many companies are held and the five that open soonest. The
    /// whole list runs to a page, and a line that long is a line nobody reads.
    /// </summary>
    public static string CapLine(IReadOnlyList<Leads.CapVerdict> capped)
    {
        var soonest = capped.OrderBy(c => c.OpensOn).ThenBy(c => c.Company, StringComparer.Ordinal).Take(5);
        return $"{capped.Count} {(capped.Count == 1 ? "company is" : "companies are")} held by RULE 50 (3 per company in 60 days, and the do-not-apply list); the next to open: {string.Join("; ", soonest.Select(c => $"{c.Company} on {c.OpensOn:yyyy-MM-dd}"))} (the tracker and systemRules.md)";
    }

    /// <summary>A log line with how long ago it was written, from its leading Central time stamp.</summary>
    public static string Age(string line, DateTime local)
    {
        if (line.Length < 19 || !DateTime.TryParseExact(line[..19], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
        {
            return Or(line, "no line yet");
        }
        var ago = local - at;
        string words = ago.TotalMinutes < 90 ? $"{Math.Max(0, (int)ago.TotalMinutes)} min ago" : ago.TotalHours < 48 ? $"{(int)ago.TotalHours} h ago" : $"{(int)ago.TotalDays} days ago";
        return $"last line {words}: {(line.Length > 20 ? line[20..].Trim() : string.Empty)}";
    }

    private static string Or(string text, string otherwise) => text.Length == 0 ? otherwise : text.Length > 220 ? text[..220] + "..." : text;

    private async Task<string> TextAsync(string path, CancellationToken ct) =>
        await store.ExistsAsync(path, ct) ? TrackerCsvText.Decode(await store.ReadAsync(path, ct)) : string.Empty;

    private async Task<string> LastLineAsync(string path, CancellationToken ct) =>
        (await TextAsync(path, ct)).Split('\n').Select(l => l.TrimEnd('\r')).LastOrDefault(l => l.Trim().Length > 0) ?? string.Empty;

    private async Task<int> CountAsync(string folder, CancellationToken ct) => (await store.ListFilesAsync(folder, ct)).Count;

    /// <summary>The next seven days' calls, or why they were not read: no Google settings, or no calendar scope on the token.</summary>
    private async Task<IReadOnlyList<string>> WeekAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (GoogleCredentials.FromEnvironment() is not { } google)
        {
            return ["Interviews: not read, the Function holds no Google settings"];
        }
        try
        {
            using var client = await google.ClientAsync(http, ct);
            string from = Uri.EscapeDataString(now.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            string to = Uri.EscapeDataString(now.AddDays(7).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            using var answer = await client.GetAsync($"https://www.googleapis.com/calendar/v3/calendars/primary/events?singleEvents=true&orderBy=startTime&timeMin={from}&timeMax={to}&maxResults=100", ct);
            if (answer.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
            {
                return ["Interviews: not read, the token holds no calendar scope yet (one consent click, on the AUTOMATE card)"];
            }
            answer.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await answer.Content.ReadAsStringAsync(ct));
            var lines = CalendarWeek.Lines(json.RootElement);
            return lines.Count == 0 ? ["No interview or call on the calendar in the next seven days (Google Calendar, primary)"] : [.. lines.Select(line => line + " (Google Calendar)")];
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return [$"Interviews: not read ({ex.GetType().Name})"];
        }
    }

    private async Task<string> CiAsync(CancellationToken ct)
    {
        try
        {
            using var client = http.CreateClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("jobhunt-card/1.0");
            using var json = JsonDocument.Parse(await client.GetStringAsync("https://api.github.com/repos/SteveStout/TheYard/actions/workflows/ci.yml/runs?branch=main&per_page=1", ct));
            var run = json.RootElement.GetProperty("workflow_runs")[0];
            string sha = run.GetProperty("head_sha").GetString()![..7];
            string conclusion = run.GetProperty("conclusion").GetString() ?? run.GetProperty("status").GetString() ?? "unknown";
            return $"CI on main: {conclusion} on {sha} (GitHub Actions, ci.yml)";
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return $"CI on main: not read ({ex.GetType().Name})";
        }
    }

    private async Task<string> VersionAsync(string host, CancellationToken ct)
    {
        try
        {
            using var client = http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            using var json = JsonDocument.Parse(await client.GetStringAsync($"https://{host}/api/version", ct));
            return $"{host}: {json.RootElement.GetProperty("version").GetString()} ({json.RootElement.GetProperty("commit").GetString()}), /api/version";
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return $"{host}: not read ({ex.GetType().Name})";
        }
    }
}
