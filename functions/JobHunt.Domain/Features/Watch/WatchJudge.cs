using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Watch;

/// <summary>
/// The watch's tests as pure judgments over what their real sources answered (the year-end plan's 7a): the tick's
/// ports read the setting, the share, the box card and Gmail, and these decide pass or fail. Each answers one
/// <see cref="WatchResult"/>, or several where a test reads one line a task or a switch.
/// </summary>
public static partial class WatchJudge
{
    /// <summary>
    /// W2, the pounce's mode after go-live: the running Function's POUNCE_RUN against the value SWITCHES.md records. Before
    /// SWITCHES.md records live, a dry pounce passes and says so; after, anything but live fails.
    /// </summary>
    /// <param name="setting">POUNCE_RUN as the tick's own process reads it.</param>
    /// <param name="recorded">POUNCE_RUN's value cell in SWITCHES.md, null when it has no line; its first word is the value.</param>
    public static WatchResult Pounce(string? setting, string? recorded)
    {
        recorded = recorded?.Trim().Split(' ', 2)[0];
        string live = string.IsNullOrWhiteSpace(setting) ? "not set (dry)" : setting.Trim();
        bool wanted = string.Equals(recorded?.Trim(), "live", StringComparison.OrdinalIgnoreCase);
        bool pass = !wanted || string.Equals(setting?.Trim(), "live", StringComparison.OrdinalIgnoreCase);
        return new WatchResult("W2", "pounce mode after go-live", pass, wanted ? live : $"{live} (go-live not recorded)", "POUNCE_RUN in the tick's environment, against J:\\dispatch\\SWITCHES.md");
    }

    /// <summary>
    /// W13, switches and secrets: each switch SWITCHES.md lists against the running Function's value (a "not set" line
    /// wants none), and each secret it lists with an expiry no nearer than 14 days. One result a mismatch; one pass
    /// line when everything matches.
    /// </summary>
    /// <param name="live">The Function's settings by name: the value for a switch, any non-null text for a set secret.</param>
    /// <param name="file">SWITCHES.md's text.</param>
    /// <param name="today">The Central day.</param>
    public static IReadOnlyList<WatchResult> Switches(IReadOnlyDictionary<string, string?> live, string file, DateOnly today)
    {
        const string Source = "the Function's settings, against J:\\dispatch\\SWITCHES.md";
        var wrong = new List<WatchResult>();
        int read = 0;
        string section = string.Empty;
        foreach (string raw in (file ?? string.Empty).Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                section = line;
                continue;
            }
            string[] cells = line.Split('|', StringSplitOptions.TrimEntries);
            if (cells.Length < 4 || !SettingName().IsMatch(cells[1]) || cells[1] == "Name")
            {
                continue;
            }
            string name = cells[1], value = cells[2];
            live.TryGetValue(name, out string? now);
            if (section.StartsWith("## Switches", StringComparison.Ordinal))
            {
                read++;
                // The value is the cell's first word: "live since 2026-11-19 14:48 CST (...)" records live.
                string? want = value.StartsWith("not set", StringComparison.OrdinalIgnoreCase) ? null : value.Split(' ', 2)[0];
                if (!string.Equals(want, string.IsNullOrWhiteSpace(now) ? null : now.Trim(), StringComparison.Ordinal))
                {
                    wrong.Add(new WatchResult("W13", name, false, $"live {now ?? "not set"}, file {want ?? "not set"}", Source));
                }
            }
            else if (section.StartsWith("## Secrets", StringComparison.Ordinal))
            {
                read++;
                if (value == "set" && string.IsNullOrEmpty(now))
                {
                    wrong.Add(new WatchResult("W13", name, false, "missing", Source));
                }
                if (Expiry().Match(line) is { Success: true } at && DateOnly.ParseExact(at.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture) is var expires
                    && expires.DayNumber - today.DayNumber <= 14)
                {
                    wrong.Add(new WatchResult("W13", name, false, $"expires {expires:yyyy-MM-dd}", Source));
                }
            }
        }
        return wrong.Count > 0 ? wrong : [new WatchResult("W13", "switches and secrets", read > 0 ? true : null, $"{read} lines match", Source)];
    }

    /// <summary>W16, no-email days: on a listed day, no mail from Gmail Sent may go to anyone but his own address.</summary>
    /// <param name="listed">Whether today is listed; null when the file would not read.</param>
    /// <param name="sentTo">The To line of each mail in Gmail Sent today.</param>
    /// <param name="own">His own address.</param>
    public static WatchResult NoEmailDay(bool? listed, IReadOnlyList<string> sentTo, string own)
    {
        const string Source = "Gmail Sent today, against J:\\dispatch\\NO-EMAIL-DAYS.txt";
        if (listed is null)
        {
            return new WatchResult("W16", "no-email day", null, "NO-EMAIL-DAYS.txt would not read, so nothing is held", Source);
        }
        int others = sentTo.Count(to => !to.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(a => a.Contains(own, StringComparison.OrdinalIgnoreCase)));
        return new WatchResult("W16", "no-email day", listed == false || others == 0, listed == true ? $"{others} to others" : "not a listed day", Source);
    }

    /// <summary>
    /// W17, the watchers: the dispatcher's HEARTBEAT inside 10 minutes, the watch's own last tick inside 30, the box
    /// card's snapshot inside 5, and no usage-limit pause over 60 minutes between 07:00 and 22:00 Central. An age that
    /// could not be read fails: a watcher that cannot be seen is not watching.
    /// </summary>
    /// <param name="heartbeat">How old HEARTBEAT is.</param>
    /// <param name="lastTick">How old the last WATCH.md is.</param>
    /// <param name="snapshot">How old the box card's snapshot is.</param>
    /// <param name="paused">How long the dispatcher has been paused, null when it is not.</param>
    /// <param name="localHour">The Central hour now.</param>
    public static IReadOnlyList<WatchResult> Watchers(TimeSpan? heartbeat, TimeSpan? lastTick, TimeSpan? snapshot, TimeSpan? paused, int localHour) =>
    [
        Age("the dispatcher's heartbeat", heartbeat, 10, "J:\\dispatch\\HEARTBEAT"),
        Age("the watch's last tick", lastTick, 30, "J:\\dispatch\\WATCH.md"),
        Age("the box card's snapshot", snapshot, 5, "/api/admin/cloud/box"),
        new WatchResult("W17", "the dispatcher's pause", paused is null || localHour < 7 || localHour >= 22 || paused <= TimeSpan.FromMinutes(60),
            paused is { } p ? $"{(int)p.TotalMinutes} min" : "not paused", "J:\\dispatch\\paused.json"),
    ];

    /// <summary>
    /// W18, the heal budget and scope, from HEALED.md: at most two code heals a Central day, and no heal opened for a
    /// heal lane's own failure.
    /// </summary>
    /// <param name="healed">HEALED.md's text.</param>
    /// <param name="today">The Central day.</param>
    public static WatchResult HealBudget(string healed, DateOnly today)
    {
        string day = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var lines = (healed ?? string.Empty).Split('\n').Where(l => l.StartsWith($"- {day} ", StringComparison.Ordinal)).ToList();
        int code = lines.Count(l => l.Contains("| 4 heal item (code) |", StringComparison.Ordinal));
        bool ofAHeal = lines.Any(l => l.Contains("| 4 heal item", StringComparison.Ordinal) && l.Split('|') is { Length: > 1 } cells && cells[1].Trim().StartsWith("heal-", StringComparison.Ordinal));
        return new WatchResult("W18", "heal budget and scope", code <= 2 && !ofAHeal, $"{code} code heals today" + (ofAHeal ? ", one for a heal lane" : string.Empty), "J:\\dispatch\\HEALED.md");
    }

    /// <summary>The five canaries J:\dispatch\CANARIES.md names, as canary.log writes them.</summary>
    public static readonly IReadOnlyList<string> CanaryLanes = ["apply", "invites", "messages", "board scan", "resume"];

    /// <summary>
    /// W8, the canaries: from 05:30 Central, each lane's last try today in canary.log reads passed or not run. Before
    /// 05:30 the morning's canaries are not due yet. A lane with no line today or whose last try failed fails.
    /// </summary>
    /// <param name="log">J:\dispatch\canary.log's text.</param>
    /// <param name="local">The Central time now.</param>
    public static IReadOnlyList<WatchResult> Canaries(string log, DateTime local)
    {
        const string Source = "J:\\dispatch\\canary.log";
        if (local.TimeOfDay < new TimeSpan(5, 30, 0))
        {
            return [new WatchResult("W8", "canaries", true, "not due before 05:30", Source)];
        }
        string today = local.ToString("ddd MM/dd", CultureInfo.InvariantCulture);
        var last = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in (log ?? string.Empty).Split('\n'))
        {
            string[] cells = line.Split(" | ");
            if (cells.Length >= 5 && cells[1].StartsWith(today, StringComparison.Ordinal))
            {
                last[cells[2]] = cells[4];
            }
        }
        return [.. CanaryLanes.Select(lane => last.TryGetValue(lane, out string? word)
            ? new WatchResult("W8", $"canary {lane}", word is "passed" or "not run", word, Source)
            : new WatchResult("W8", $"canary {lane}", false, "no canary today", Source))];
    }

    /// <summary>
    /// W14, what reached him: the card as written holds no "Queue it", no EMPTY RUN line and no "Proposed:" line, the
    /// roads RULE 77 and the plan's 7c close.
    /// </summary>
    /// <param name="card">TODAY.md as written.</param>
    public static WatchResult Reached(string card)
    {
        int roads = (card ?? string.Empty).Split('\n').Count(line => line.Contains("Queue it", StringComparison.OrdinalIgnoreCase)
            || line.Contains("EMPTY RUN", StringComparison.Ordinal) || line.TrimStart('-', ' ', '*').StartsWith("Proposed:", StringComparison.Ordinal));
        return new WatchResult("W14", "what reached him", roads == 0, $"{roads} lines outside his two jobs", "J:\\TODAY.md as written");
    }

    /// <summary>The rows under a JOBS file's READY TO APPLY heading.</summary>
    /// <param name="jobs">The JOBS file's text.</param>
    public static int ReadyRows(string jobs)
    {
        int rows = 0;
        bool ready = false;
        foreach (string line in (jobs ?? string.Empty).Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                ready = line.StartsWith("## READY TO APPLY", StringComparison.Ordinal);
                continue;
            }
            rows += ready && ReadyRow().IsMatch(line) ? 1 : 0;
        }
        return rows;
    }

    /// <summary>
    /// W15, supply: the night's candidates file written by 02:30 Central, and READY above zero on at least one of the last
    /// two nights' JOBS files.
    /// </summary>
    /// <param name="written">When today's nightly/jobs-candidates file was written, Central; null when there is none.</param>
    /// <param name="readyTonight">Today's JOBS file's READY rows, null when there is no file.</param>
    /// <param name="readyLastNight">Yesterday's.</param>
    /// <param name="local">The Central time now.</param>
    public static WatchResult Supply(DateTime? written, int? readyTonight, int? readyLastNight, DateTime local)
    {
        var due = local.Date.AddHours(2).AddMinutes(30);
        bool file = local < due || written is { } at && at <= due && at.Date == local.Date;
        bool ready = (readyTonight ?? 0) > 0 || (readyLastNight ?? 0) > 0 || local < due;
        return new WatchResult("W15", "supply", file && ready,
            $"candidates {(written is { } w ? w.ToString("HH:mm", CultureInfo.InvariantCulture) : "not written")}, READY {readyTonight?.ToString(CultureInfo.InvariantCulture) ?? "no file"} and {readyLastNight?.ToString(CultureInfo.InvariantCulture) ?? "no file"}",
            "J:\\nightly\\jobs-candidates-<date>.tsv and J:\\JOBS-<date>.md");
    }

    private static WatchResult Age(string name, TimeSpan? age, int minutes, string source) =>
        new("W17", name, age is { } a && a <= TimeSpan.FromMinutes(minutes), age is { } b ? $"{(int)b.TotalMinutes} min" : "not read", source);

    [GeneratedRegex(@"^\|\s*\d+\s*\|")]
    private static partial Regex ReadyRow();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z_]+$")]
    private static partial Regex SettingName();

    [GeneratedRegex(@"\|\s*(20\d\d-\d\d-\d\d)\b")]
    private static partial Regex Expiry();
}
