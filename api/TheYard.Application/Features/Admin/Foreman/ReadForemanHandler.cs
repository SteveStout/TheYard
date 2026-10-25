using System.Globalization;
using System.Text;
using TheYard.Application.Shared;

namespace TheYard.Application.Features.Admin.Foreman;

// #region foreman
/// <summary>
/// Port: what the Foreman reads, read only (ADR: The Foreman, a place to start): every card under Cloud
/// infrastructure as it reads now, and the build that answers.
/// </summary>
public interface IForemanReadings
{
    /// <summary>Every Cloud infrastructure card the Foreman reads, as each reads for the last 24 hours.</summary>
    /// <param name="cancellation">The request's cancellation.</param>
    Task<IReadOnlyList<ForemanCard>> CardsAsync(CancellationToken cancellation);

    /// <summary>The failures Application Insights kept in the last seven days, newest first.</summary>
    /// <param name="cancellation">The request's cancellation.</param>
    Task<ForemanFailures> FailuresAsync(CancellationToken cancellation);

    /// <summary>The version this process runs, when it started, and now, in milliseconds since the epoch, UTC.</summary>
    ForemanBuild Build();
}

/// <summary>One kind of failure, as the Foreman cites it.</summary>
/// <param name="Role">The app, by its cloud role name.</param>
/// <param name="What">The exception's type, or the status answered.</param>
/// <param name="Where">The operation or route.</param>
/// <param name="Count">How many.</param>
/// <param name="LastMs">The newest, in milliseconds since the epoch, UTC.</param>
public sealed record ForemanFailure(string Role, string What, string Where, long Count, long LastMs);

/// <summary>The failures as read.</summary>
/// <param name="Available">False when Application Insights was not read.</param>
/// <param name="Note">Why it was not, or null.</param>
/// <param name="Rows">The failures, newest first.</param>
public sealed record ForemanFailures(bool Available, string? Note, IReadOnlyList<ForemanFailure> Rows);

/// <summary>The build that answers.</summary>
/// <param name="Version">The changelog's top line it was built from.</param>
/// <param name="StartedMs">When this process started.</param>
/// <param name="NowMs">Now, on this process's clock.</param>
public sealed record ForemanBuild(string Version, long StartedMs, long NowMs);

/// <summary>One card as it reads.</summary>
/// <param name="Key">Its slug, which ?view=admin&amp;card= opens.</param>
/// <param name="Title">Its title.</param>
/// <param name="Tone">fine, amber, red or unread, the card's own.</param>
/// <param name="Headline">The rail's figure, or null.</param>
/// <param name="Note">What the card says it could not do, or null.</param>
public sealed record ForemanCard(string Key, string Title, string Tone, string? Headline, string? Note);

/// <summary>Where a developer starts on a card that moved: the record that governs it, its code and its test.</summary>
/// <param name="Card">The card's title.</param>
/// <param name="Record">The decision record, by number and title.</param>
/// <param name="File">The file the card's reading is made in.</param>
/// <param name="Test">The test that holds the card.</param>
public sealed record ForemanStart(string Card, string Record, string File, string Test);

/// <summary>The Foreman's page, in the five sections every page keeps, and the same page as markdown.</summary>
/// <param name="AsOfMs">When it was read.</param>
/// <param name="Version">The version that answered.</param>
/// <param name="What">What happened, one sentence with the window.</param>
/// <param name="Readings">The cards that are not fine, with their figures.</param>
/// <param name="Cause">The likely cause, or no cause with confidence and what was ruled out.</param>
/// <param name="Start">Where to start, a row a card that moved.</param>
/// <param name="CouldNotRead">What it could not read, so the gap is visible.</param>
/// <param name="Markdown">The page as docs/foreman keeps it.</param>
public sealed record ForemanPage(long AsOfMs, string Version, string What, IReadOnlyList<ForemanCard> Readings, string Cause, IReadOnlyList<ForemanStart> Start, IReadOnlyList<string> CouldNotRead, string Markdown);

/// <summary>Asks for the Foreman's page.</summary>
public sealed record ReadForemanQuery;

/// <summary>
/// Rung 0 of the Foreman: observe and explain, with no model. Every sentence is computed from the cards' own
/// readings and the failures Application Insights kept; nothing is typed by hand, and nothing is acted on.
/// </summary>
/// <param name="readings">The cards, the failures and the build.</param>
public sealed class ReadForemanHandler(IForemanReadings readings) : IQueryHandler<ReadForemanQuery, ForemanPage>
{
    /// <summary>The page.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<ForemanPage> HandleAsync(ReadForemanQuery query, CancellationToken cancellation) =>
        ForemanPages.Compose(await readings.CardsAsync(cancellation), await readings.FailuresAsync(cancellation), readings.Build());
}

/// <summary>The page from its readings: pure, so a recorded night reads back to the same page.</summary>
public static class ForemanPages
{
    /// <summary>Where to start on each card: the record that governs it, its code and its test, each a file in this repository.</summary>
    public static readonly IReadOnlyDictionary<string, (string Record, string File, string Test)> Starts = new Dictionary<string, (string, string, string)>(StringComparer.Ordinal)
    {
        ["sqldb"] = ("ADR-102: One frame for every resource", "api/TheYard.Api/Features/Admin/Cloud/SqlCard.cs", "api/TheYard.Tests/CloudThresholdTests.cs"),
        ["cosmosdb"] = ("ADR-102: One frame for every resource", "api/TheYard.Api/Features/Admin/Cloud/CosmosCard.cs", "api/TheYard.Tests/CloudReadersTests.cs"),
        ["registry"] = ("ADR-102: One frame for every resource", "api/TheYard.Api/Features/Admin/Cloud/AcrCard.cs", "api/TheYard.Tests/CardFrameTests.Rows.cs"),
        ["insights"] = ("ADR-102: One frame for every resource", "api/TheYard.Api/Features/Admin/Cloud/InsightsCard.cs", "api/TheYard.Tests/CardFrameTests.Rows.cs"),
        ["share"] = ("ADR-102: One frame for every resource", "api/TheYard.Api/Features/Admin/Cloud/ShareCard.cs", "api/TheYard.Tests/StorageCardTests.cs"),
        ["blob"] = ("ADR-102: One frame for every resource", "api/TheYard.Api/Features/Admin/Cloud/BlobCard.cs", "api/TheYard.Tests/StorageCardTests.cs"),
        ["mail"] = ("ADR-102: One frame for every resource", "api/TheYard.Api/Features/Admin/Cloud/MailCard.cs", "api/TheYard.Tests/CardFrameTests.Rows.cs"),
        ["edge"] = ("ADR-007: Edge deploy economics", "api/TheYard.Api/Features/Admin/Cloud/EdgeCard.cs", "api/TheYard.Tests/CardFrameTests.Rows.cs"),
        ["nightly"] = ("ADR-109: The Nightly Function on the Admin tab", "api/TheYard.Api/Features/Admin/Cloud/NightlyCard.cs", "api/TheYard.Tests/NightlyCardTests.Kept.cs"),
        ["claude"] = ("ADR-112: Every Claude lane reports its usage", "api/TheYard.Api/Features/Admin/Cloud/ClaudeCard.cs", "api/TheYard.Tests/ClaudeCardTests.cs"),
        ["campaign"] = ("ADR-118: The campaign sends, the model writes rows", "api/TheYard.Api/Features/Admin/Cloud/CampaignCard.cs", "api/TheYard.Tests/CampaignCardTests.cs"),
    };

    /// <summary>The hour after a start in which a card may still be unread because it has not had its first reading.</summary>
    public const long FirstHourMs = 3_600_000;

    /// <summary>The page.</summary>
    /// <param name="cards">Every card as it reads.</param>
    /// <param name="failures">The failures in the last seven days.</param>
    /// <param name="build">The build that answers.</param>
    public static ForemanPage Compose(IReadOnlyList<ForemanCard> cards, ForemanFailures failures, ForemanBuild build)
    {
        var moved = cards.Where(card => card.Tone != "fine").ToList();
        int red = cards.Count(card => card.Tone == "red"), amber = cards.Count(card => card.Tone == "amber"), unread = cards.Count(card => card.Tone == "unread");
        long lastHour = build.NowMs - 3_600_000;
        long failuresWeek = failures.Rows.Sum(row => row.Count), failuresHour = failures.Rows.Where(row => row.LastMs >= lastHour).Sum(row => row.Count);
        long sinceStart = build.NowMs - build.StartedMs;

        string what = $"In the 24 hours to {Central(build.NowMs)} Central, {red} of {cards.Count} Cloud infrastructure cards read red, {amber} amber and {unread} unread; "
            + (failures.Available ? $"Application Insights kept {Plural(failuresWeek, "failure")} in 7 days, {failuresHour} in the last hour; " : "Application Insights was not read; ")
            + $"this process has run {build.Version} since {Central(build.StartedMs)} Central.";

        var agree = new List<string>();
        if (sinceStart < FirstHourMs && unread > 0)
        {
            agree.Add($"this process started {sinceStart / 60_000} minutes ago, and a card read in its first hour can be unread until its first reading");
        }
        if (failures.Available && failures.Rows.FirstOrDefault(row => row.LastMs >= lastHour) is { } newest)
        {
            agree.Add($"the newest failure, {newest.What} in {newest.Where} on {newest.Role}, was at {Central(newest.LastMs)} Central");
        }
        string ruledOut = string.Join("; ", new[]
        {
            sinceStart >= FirstHourMs ? $"a start in the last hour (this process started {Central(build.StartedMs)})" : null,
            failures.Available && failuresHour == 0 ? "a failure in the last hour" : null,
        }.Where(item => item is not null));
        // Rung 0 names a cause only with three agreeing sources, and with no model it has at most two: the nearest start
        // and the newest failure. So it says what is nearest and what it ruled out, and leaves the cause to the reader.
        string cause = moved.Count == 0
            ? "Nothing moved: every card reads fine."
            : "No cause with confidence." + (agree.Count > 0 ? " Nearest: " + string.Join("; ", agree) + "." : "") + (ruledOut.Length > 0 ? " Ruled out: " + ruledOut + "." : "");

        var start = moved.Where(card => Starts.ContainsKey(card.Key))
            .Select(card => new ForemanStart(card.Title, Starts[card.Key].Record, Starts[card.Key].File, Starts[card.Key].Test)).ToList();
        var couldNot = cards.Where(card => card.Tone == "unread").Select(card => $"{card.Title}: {card.Note ?? "not read yet"}").ToList();
        if (!failures.Available)
        {
            couldNot.Add($"Application Insights: {failures.Note}");
        }

        return new ForemanPage(build.NowMs, build.Version, what, moved, cause, start, couldNot, Markdown(build, what, moved, cause, start, couldNot));
    }

    private static string Markdown(ForemanBuild build, string what, IReadOnlyList<ForemanCard> moved, string cause, IReadOnlyList<ForemanStart> start, IReadOnlyList<string> couldNot)
    {
        var page = new StringBuilder();
        page.Append(CultureInfo.InvariantCulture, $"# The Foreman, {Day(build.NowMs)}\n\nRead {Central(build.NowMs)} Central from {build.Version}, rung 0: observed and explained, nothing acted on, no model.\n\n");
        page.Append(CultureInfo.InvariantCulture, $"## What happened\n\n{what}\n\n## The readings\n\n");
        page.Append(moved.Count == 0 ? "Every card reads fine.\n\n" : "| Card | Tone | Figure | Note | Source |\n| --- | --- | --- | --- | --- |\n"
            + string.Concat(moved.Select(card => $"| {Cell(card.Title)} | {card.Tone} | {Cell(card.Headline ?? "")} | {Cell(card.Note ?? "")} | /api/admin/cloud/{card.Key}?window=24h |\n")) + "\n");
        page.Append(CultureInfo.InvariantCulture, $"## The likely cause\n\n{cause}\n\n## Where to start\n\n");
        page.Append(start.Count == 0 ? "Nowhere: nothing moved.\n\n" : "| Card | Record | File | Test |\n| --- | --- | --- | --- |\n"
            + string.Concat(start.Select(row => $"| {Cell(row.Card)} | {row.Record} | {row.File} | {row.Test} |\n")) + "\n");
        page.Append("## What it could not read\n\n");
        page.Append(couldNot.Count == 0 ? "Nothing: every card and Application Insights answered.\n" : string.Concat(couldNot.Select(line => $"- {line}\n")));
        return page.ToString();
    }

    private static readonly TimeZoneInfo CentralZone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private static string Central(long ms) => TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(ms), CentralZone).ToString("ddd d MMM HH:mm", CultureInfo.InvariantCulture);

    private static string Day(long ms) => TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(ms), CentralZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Plural(long count, string word) => $"{count} {word}{(count == 1 ? "" : "s")}";

    private static string Cell(string text) => text.Replace("|", "/", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
// #endregion foreman
