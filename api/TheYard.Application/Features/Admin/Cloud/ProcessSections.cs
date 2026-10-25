// The nightly card's seven process sections (ADR-109, the addendum on the six processes; ADR-102, the addendum on
// the two slots): each process's switches from the Function's health route, its summary from its own code, and
// its last run, thirty days and day strip from the runs reader's fold. Pure: the fold, the switches, the
// summaries and the moment go in, the sections come out, so a test holds every figure without a workspace.
using System.Globalization;
using TheYard.Application.Shared.Cards;
using TheYard.Domain.Features.Admin;

namespace TheYard.Application.Features.Admin.Cloud;

// #region process-sections
/// <summary>Builds the seven sections from what was read.</summary>
public static class ProcessSections
{
    /// <summary>How many days the strip shows, one cell a Central day, today last.</summary>
    public const int StripDays = 30;

    /// <summary>Where the run figures come from.</summary>
    public const string RunSource = "Application Insights, the run line", FoldSource = "Application Insights, requests by name";

    /// <summary>The seven sections, in the card's order.</summary>
    /// <param name="fold">Thirty days of runs by process and day, and each process's newest run.</param>
    /// <param name="switches">The switches by name as the health route answered them; null when it did not answer.</param>
    /// <param name="summary">Each process's summary paragraph, by its key.</param>
    /// <param name="allHref">Each process's link to every run, by its key.</param>
    /// <param name="now">The moment the card is built, UTC.</param>
    public static IReadOnlyList<CloudProcess> Build(ProcessFold fold, IReadOnlyDictionary<string, string>? switches, Func<string, string> summary, Func<string, string?> allHref, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(NightlySlots.Local(now));
        return [.. FunctionProcesses.All.Select(process =>
        {
            var days = fold.Days.Where(day => day.Process == process.Key).ToDictionary(day => day.Day, day => day.Tally);
            var month = Sum(days.Values);
            fold.Last.TryGetValue(process.Key, out var last);
            fold.Last.TryGetValue("MergerHealth", out var health);
            var chips = Chips(process, switches);
            return new CloudProcess(
                process.Key,
                process.Title,
                Tone(process, chips),
                chips,
                summary(process.Key),
                process.Schedule,
                [
                    new CloudFigure(process.Key == "MergerHealth" ? "Last answer" : "Last run", LastRun(last, fold), last is { HasLine: false } ? "Application Insights, the request row" : RunSource),
                    Second(process, last, health),
                    new CloudFigure("30 days", fold.Available ? ProcessWords.ThirtyDays(process, month) : "not read", FoldSource),
                    new CloudFigure("Next", NextWords(FunctionProcesses.Next(process, now), now), process.Key switch
                    {
                        "Nightly" => "the clock's slots",
                        "MergerHealth" => "this card's next read",
                        _ => "the timer's schedule",
                    }),
                ],
                [.. Enumerable.Range(0, StripDays).Select(i => ProcessWords.Cell(days.GetValueOrDefault(today.AddDays(i - StripDays + 1))))],
                process.Key == "MergerHealth" ? "answers, 30 days" : "runs, 30 days",
                ProcessWords.N(month.Runs),
                allHref(process.Key),
                ProcessWords.CountLine(process, month, "30 days"));
        })];
    }

    /// <summary>Days' totals added together; the 95th is the worst day's.</summary>
    public static ProcessTally Sum(IEnumerable<ProcessTally> days)
    {
        var all = days.ToList();
        return new ProcessTally(all.Sum(d => d.Runs), all.Sum(d => d.Shown), all.Sum(d => d.Acted), all.Sum(d => d.Late), all.Sum(d => d.Failed), all.Sum(d => d.Blocked), all.Sum(d => d.Planned), all.Max(d => d.P95Ms));
    }

    /// <summary>A process's switches as chips: on is fine, shadow info, anything else off; the health route has none and is always on.</summary>
    public static IReadOnlyList<CloudSwitch> Chips(FunctionProcess process, IReadOnlyDictionary<string, string>? switches)
    {
        if (process.Switches.Count == 0)
        {
            return [new CloudSwitch("always", "on", "fine")];
        }
        return [.. process.Switches.Select(name =>
        {
            string value = switches is null ? "not read" : switches.GetValueOrDefault(name) ?? "off";
            return new CloudSwitch(name, value, value switch { "on" => "fine", "shadow" => "info", _ => "off" });
        })];
    }

    /// <summary>A section's tone from its first switch; the clock's one switch only widens its hours, so the clock is good while it runs.</summary>
    private static string Tone(FunctionProcess process, IReadOnlyList<CloudSwitch> chips) =>
        process.Key == "Nightly" ? "good" : chips[0].Tone switch { "fine" => "good", "info" => "info", _ => "off" };

    private static string LastRun(ProcessRun? last, ProcessFold fold) =>
        last is null ? fold.Available ? "no run in 30 days" : fold.Note ?? "not read"
            : $"{ProcessWords.Clock(last.At)} Central · {ProcessWords.Took(last.TookMs)} · {ProcessWords.Came(last)}";

    /// <summary>The second figure: what the last run read and wrote, or for the merger and its health route, the health route's own counts.</summary>
    private static CloudFigure Second(FunctionProcess process, ProcessRun? last, ProcessRun? health) => process.Key switch
    {
        "Merger" => new("Keeping up", health is null ? "not read" : ProcessWords.Sentence("MergerHealth", health), "the Function's health route, counts and ages only"),
        "MergerHealth" => new("It answered", health is null ? "not read" : ProcessWords.Sentence("MergerHealth", health), "the route's own counts"),
        _ => new("It read and wrote", last is null ? "not read" : ProcessWords.Sentence(process.Key, last), "the run line, counts only"),
    };

    /// <summary>"11:50 Central" today, "Sun 00:00 Central" on another day.</summary>
    private static string NextWords(DateTimeOffset next, DateTimeOffset now) =>
        NightlySlots.Local(next).Date == NightlySlots.Local(now).Date ? $"{ProcessWords.Clock(next)} Central" : $"{NightlySlots.Local(next).ToString("ddd", CultureInfo.InvariantCulture)} {ProcessWords.Clock(next)} Central";
}
// #endregion process-sections
