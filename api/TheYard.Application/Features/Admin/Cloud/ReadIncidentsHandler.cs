using System.Text.RegularExpressions;
using TheYard.Application.Shared;

namespace TheYard.Application.Features.Admin.Cloud;

// #region incidents
/// <summary>
/// Port: what broke, read only. The incidents the docs keep (ADR: A red that lasts an hour gets a postmortem), and the
/// failures Application Insights kept: exceptions, and answers of 500 or more, across every app that sends to it.
/// </summary>
public interface IIncidentsReader
{
    /// <summary>The incidents index as it is written, a row an incident.</summary>
    string IncidentsIndex();

    /// <summary>The failures in a window, newest first, at most <see cref="ReadIncidentsHandler.MaxFailures"/>.</summary>
    /// <param name="window">How far back.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    Task<FailuresRead> FailuresAsync(TimeSpan window, CancellationToken cancellation);
}

/// <summary>One incident as the index lists it.</summary>
/// <param name="Id">INC-001 and on.</param>
/// <param name="Title">Its title.</param>
/// <param name="Dates">When it ran.</param>
/// <param name="HoursRed">How long a card read red, in the index's words.</param>
/// <param name="Card">The card that went red, in the index's words.</param>
/// <param name="FixedIn">The versions that fixed it.</param>
/// <param name="Slug">The document's slug, which ?doc= opens.</param>
public sealed record IncidentRow(string Id, string Title, string Dates, string HoursRed, string Card, string FixedIn, string Slug);

/// <summary>One kind of failure in the window.</summary>
/// <param name="Role">The app that failed, by its cloud role name.</param>
/// <param name="Kind">"exception" or "answer".</param>
/// <param name="What">The exception's type, or the status the app answered.</param>
/// <param name="Where">The operation or route it happened in.</param>
/// <param name="Count">How many, sampled rows counted for what they stand for.</param>
/// <param name="LastMs">The newest, in milliseconds since the epoch, UTC.</param>
public sealed record FailureRow(string Role, string Kind, string What, string Where, long Count, long LastMs);

/// <summary>The failures as read.</summary>
/// <param name="Available">False when Application Insights was not read; <paramref name="Note"/> says why.</param>
/// <param name="Note">Why nothing was read, or null.</param>
/// <param name="ReadAtMs">When it was read, in milliseconds since the epoch, UTC.</param>
/// <param name="Rows">The failures, newest first.</param>
public sealed record FailuresRead(bool Available, string? Note, long ReadAtMs, IReadOnlyList<FailureRow> Rows);

/// <summary>The card's answer: the incidents, then the failures.</summary>
/// <param name="AsOfMs">When the failures were read.</param>
/// <param name="Window">The failures' window, "7d".</param>
/// <param name="Incidents">Every incident the index lists, in its order.</param>
/// <param name="FailuresAvailable">False when Application Insights was not read.</param>
/// <param name="FailuresNote">Why it was not, or null.</param>
/// <param name="Failures">The failures, newest first.</param>
/// <param name="Headline">The card's figure in a few words.</param>
/// <param name="Tone">fine, amber, red or unread: red with a failure in the last hour, amber with one in the window.</param>
public sealed record IncidentsReport(
    long AsOfMs,
    string Window,
    IReadOnlyList<IncidentRow> Incidents,
    bool FailuresAvailable,
    string? FailuresNote,
    IReadOnlyList<FailureRow> Failures,
    string Headline,
    string Tone);

/// <summary>Asks for the incidents and the window's failures.</summary>
public sealed record ReadIncidentsQuery;

/// <summary>The incidents the docs keep and the failures Application Insights kept, nothing typed by hand.</summary>
/// <param name="reader">The index and the workspace.</param>
public sealed partial class ReadIncidentsHandler(IIncidentsReader reader) : IQueryHandler<ReadIncidentsQuery, IncidentsReport>
{
    /// <summary>The failures' window.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    /// <summary>The most failure rows the card lists.</summary>
    public const int MaxFailures = 30;

    /// <summary>The incidents and the failures.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<IncidentsReport> HandleAsync(ReadIncidentsQuery query, CancellationToken cancellation)
    {
        var incidents = Parse(reader.IncidentsIndex());
        var failures = await reader.FailuresAsync(Window, cancellation);
        long hourAgo = failures.ReadAtMs - 3_600_000;
        string tone = !failures.Available ? "unread"
            : failures.Rows.Any(row => row.LastMs >= hourAgo) ? "red"
            : failures.Rows.Count > 0 ? "amber"
            : "fine";
        long total = failures.Rows.Sum(row => row.Count);
        string headline = !failures.Available ? $"{incidents.Count} incident{(incidents.Count == 1 ? "" : "s")}"
            : $"{total} failure{(total == 1 ? "" : "s")} in 7 days";
        return new IncidentsReport(failures.ReadAtMs, "7d", incidents, failures.Available, failures.Note, [.. failures.Rows.Take(MaxFailures)], headline, tone);
    }

    /// <summary>The rows of the index's table, each linked to its document; a row in any other shape is not an incident.</summary>
    /// <param name="index">INCIDENTS.md as written.</param>
    public static IReadOnlyList<IncidentRow> Parse(string index) =>
        [.. index.Split('\n')
            .Select(line => Row().Match(line.Trim()))
            .Where(match => match.Success)
            .Select(match => new IncidentRow(
                match.Groups["id"].Value,
                match.Groups["title"].Value.Trim(),
                match.Groups["dates"].Value.Trim(),
                match.Groups["hours"].Value.Trim(),
                match.Groups["card"].Value.Trim(),
                match.Groups["fixed"].Value.Trim(),
                match.Groups["slug"].Value))];

    [GeneratedRegex(@"^\| \[(?<id>INC-\d{3})\]\([^)\s]*\?doc=(?<slug>[a-z0-9-]+)\) \|(?<title>[^|]+)\|(?<dates>[^|]+)\|(?<hours>[^|]+)\|(?<card>[^|]+)\|(?<fixed>[^|]+)\|$")]
    private static partial Regex Row();
}
// #endregion incidents
