namespace JobHunt.Domain.Features.Watch;

/// <summary>
/// W9 and W12 (the year-end plan's 7a) judged from what the site's public card routes and GitHub's API answered: the
/// site up on main's commit with CI green, and every scheduled task's heartbeat. A source that did not answer is not
/// read, never a pass.
/// </summary>
public static class WatchBeats
{
    /// <summary>How long a new main may take to reach both names and its CI, before a mismatch is a failure.</summary>
    public static readonly TimeSpan Rolling = TimeSpan.FromMinutes(30);

    /// <summary>
    /// W9, TheYard up: both names answer main's commit and CI on main is green. Inside 30 minutes of main's commit a
    /// name or a CI run still behind is rolling, not a failure; after that it fails.
    /// </summary>
    /// <param name="one">The commit theyard's /api/version answered, null when it did not answer.</param>
    /// <param name="cosmos">The commit theyard-cosmos's /api/version answered.</param>
    /// <param name="main">Main's head commit from GitHub, null when GitHub did not answer.</param>
    /// <param name="mainAt">When that commit was made.</param>
    /// <param name="ci">CI's conclusion for main's commit ("success", "failure"), null while it runs or when none is listed.</param>
    /// <param name="now">The instant.</param>
    public static WatchResult Site(string? one, string? cosmos, string? main, DateTimeOffset? mainAt, string? ci, DateTimeOffset now)
    {
        const string Source = "/api/version on both names, main and CI from the GitHub API";
        if (main is null)
        {
            return new WatchResult("W9", "TheYard up", null, "GitHub did not answer", Source);
        }
        string head = main.Length >= 7 ? main[..7] : main;
        bool on(string? commit) => commit is { Length: >= 7 } c && c[..7] == head;
        bool rolling = mainAt is { } at && now - at < Rolling;
        string number = $"theyard {one ?? "no answer"}, theyard-cosmos {cosmos ?? "no answer"}, main {head}, CI {ci ?? "running"}";
        if (on(one) && on(cosmos) && ci == "success")
        {
            return new WatchResult("W9", "TheYard up", true, number, Source);
        }
        return new WatchResult("W9", "TheYard up", ci == "failure" || !rolling ? false : null, rolling && ci != "failure" ? number + " (rolling)" : number, Source);
    }

    /// <summary>
    /// W6, the four fixed slots, each judged once its due time has passed: the 04:00 reply lane's DONE note by 04:45, the
    /// 06:00 run's card inside its four-hour ceiling (by 10:00), the network log's 06:00 run by 06:15, and the 07:30 card's
    /// run by 07:45, each from its own record. A slot not due yet passes and says so; a due slot with nothing today fails.
    /// </summary>
    /// <param name="local">The Central time now.</param>
    /// <param name="reply">When today's 0400-reply DONE note was written, Central.</param>
    /// <param name="morning">When today's 0600-card.md was written.</param>
    /// <param name="network">When the network log's run today was, from its run line.</param>
    /// <param name="card">When the morning card's run was today, from its run line.</param>
    public static IReadOnlyList<WatchResult> Slots(DateTime local, DateTime? reply, DateTime? morning, DateTime? network, DateTime? card) =>
    [
        Slot("the 04:00 reply lane", local, new TimeSpan(4, 45, 0), reply, "J:\\nightly\\<day>\\0400-reply-*\\DONE.md"),
        Slot("the 06:00 run", local, new TimeSpan(10, 0, 0), morning, "J:\\nightly\\<day>\\0600-card.md"),
        Slot("the 06:00 network log", local, new TimeSpan(6, 15, 0), network, "/api/admin/cloud/nightly/runs/NetworkLogPass"),
        Slot("the 07:30 card", local, new TimeSpan(7, 45, 0), card, "/api/admin/cloud/nightly/runs/TodayCard (the mail itself is not read by this tick yet)"),
    ];

    /// <summary>W11, the review route: opens reaching a second action above 30 percent; below it is a January item, never a heal.</summary>
    /// <param name="opened">Opens of the first screen in the window, null when the route did not answer.</param>
    /// <param name="second">Opens that reached a second action.</param>
    public static WatchResult Review(int? opened, int? second) =>
        opened is not { } o || second is not { } s
            ? new WatchResult("W11", "review route conversion", null, "not read", "/api/admin/review")
            : new WatchResult("W11", "review route conversion", o > 0 && s * 100 > o * 30, $"{s} of {o} reached a second action", "/api/admin/review");

    private static WatchResult Slot(string name, DateTime local, TimeSpan due, DateTime? written, string source)
    {
        if (local.TimeOfDay < due)
        {
            return new WatchResult("W6", name, true, "not due yet", source);
        }
        bool today = written is { } at && at.Date == local.Date;
        return new WatchResult("W6", name, today && written!.Value.TimeOfDay <= due, today ? written!.Value.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "nothing today", source);
    }

    /// <summary>W12 for one row of the nightly card (firing, merger): good and info pass; anything else fails.</summary>
    /// <param name="key">The row's key.</param>
    /// <param name="tone">Its tone.</param>
    /// <param name="answer">Its one-line answer.</param>
    public static WatchResult CardRow(string key, string tone, string answer) =>
        new("W12", $"the Function: {key}", tone is "good" or "info", answer, "/api/admin/cloud/nightly");

    /// <summary>W12 for one Function process: its newest listed run must not have failed or come to blocked.</summary>
    /// <param name="process">The process's name.</param>
    /// <param name="outcome">Its newest listed run's outcome, null when none is listed.</param>
    /// <param name="when">That run's time as the card prints it.</param>
    public static WatchResult Process(string process, string? outcome, string? when) =>
        outcome is null
            ? new WatchResult("W12", process, null, "no run listed", $"/api/admin/cloud/nightly/runs/{process}")
            : new WatchResult("W12", process, outcome is not ("failed" or "blocked"), $"{outcome} at {when}", $"/api/admin/cloud/nightly/runs/{process}");

    /// <summary>
    /// W12 for one unit on the box card: a timer or a long-running service is active; a oneshot service the box runs on a
    /// timer may be inactive between its runs, and only its failed state fails it.
    /// </summary>
    /// <param name="unit">The unit, "jobhunt-dispatch.service (user)".</param>
    /// <param name="state">Its state as the card prints it, "active, running".</param>
    public static WatchResult Unit(string unit, string state)
    {
        string name = unit.Split(' ')[0];
        bool oneshot = name is "jobhunt-6am.service";
        bool pass = state.StartsWith("active", StringComparison.Ordinal) || (oneshot && !state.Contains("failed", StringComparison.Ordinal));
        return new WatchResult("W12", name, pass, state, "/api/admin/cloud/box, the services row");
    }
}
