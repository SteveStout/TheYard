using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Records;

/// <summary>A verified or claimed send, ready to become a tracker row.</summary>
/// <param name="Row">The worklist row.</param>
/// <param name="Reported">The model's report on it.</param>
/// <param name="Text">The text that went out, placeholders filled; empty for an application.</param>
/// <param name="Missing">What the evidence lacked, empty when verified.</param>
/// <param name="Model">The model that ran the lane.</param>
/// <param name="Screenshot">The screenshot's copy in the run folder.</param>
public sealed record SendRecord(WorkRow Row, RowResult Reported, string Text, IReadOnlyList<string> Missing, string Model, string Screenshot);

/// <summary>
/// One new file for tracker-inbox per send, in SHARED-WRITES.md's six columns, one row. The file's name is built from
/// the UTC time and the lane, so no other writer can collide with it, and the merger assigns the Count and stamps the
/// receipt tag. The Notes open with the words the counts read, so this process's own rows count on the next run.
/// </summary>
public static partial class TrackerRowFile
{
    /// <summary>The file's name.</summary>
    /// <param name="at">The instant of the send.</param>
    /// <param name="what">The person or company.</param>
    public static string Name(DateTimeOffset at, string what) => $"{RunSlot.Stamp(at)}-{RegistryRows.Lane}-{Slug(what)}.csv";

    /// <summary>The file's one line.</summary>
    /// <param name="record">The send.</param>
    /// <param name="at">The instant of the send.</param>
    public static string Line(SendRecord record, DateTimeOffset at)
    {
        DateTime local = RunSlot.Local(at);
        string person = record.Reported.Person is { Length: > 0 } found ? found : (record.Row.Person.Length > 0 ? record.Row.Person : "NA");
        string channel = record.Row.Action == WorkAction.Apply ? Board(record.Row.Url) : "LinkedIn";
        string line = TrackerCsvText.Line([person, record.Row.Company, channel, local.ToString("M/d/yyyy", CultureInfo.InvariantCulture), string.Empty, Notes(record, local)]);
        return line + "\r\n";
    }

    private static string Notes(SendRecord record, DateTime local)
    {
        string head = record.Row.Action switch
        {
            WorkAction.Invite => "CONNECTION REQUEST SENT by Claude",
            WorkAction.Message => "FOLLOW-UP MESSAGE SENT by Claude",
            _ => "APPLICATION SUBMITTED by Claude",
        };
        string state = record.Missing.Count == 0 ? "Verified" : $"UNVERIFIED, read the page before any further touch ({string.Join("; ", record.Missing)})";
        RowEvidence? evidence = record.Reported.Evidence;
        var parts = new List<string>
        {
            $"{head} ({RegistryRows.Lane}, the 6 AM timer, WickedAwsome Chrome, {record.Model}) {local.ToString("HH:mm", CultureInfo.InvariantCulture)} Central. {state}.",
        };
        AddWhen(parts, record.Reported.Title, "Title as read: {0}.");
        AddWhen(parts, record.Reported.ProfileUrl ?? record.Row.Url, "Page {0}.");
        AddWhen(parts, evidence?.Toast, "Toast: {0}.");
        AddWhen(parts, evidence?.Confirmation, "Confirmation: {0}.");
        AddWhen(parts, record.Text, "Text sent: {0}");
        AddWhen(parts, record.Row.Code, "Code {0}.");
        AddWhen(parts, record.Row.Resume is { } resume ? $"headline \"{resume.Headline}\", {Path.GetFileName(resume.Path)} SHA-256 {resume.Sha}" : null, "Resume {0}.");
        AddWhen(parts, record.Screenshot, "Screenshot {0}.");
        parts.Add($"Source {record.Row.Source}.");
        return string.Join(' ', parts);
    }

    private static void AddWhen(List<string> parts, string? value, string shape)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(string.Format(CultureInfo.InvariantCulture, shape, value));
        }
    }

    private static string Board(string url) => url switch
    {
        _ when url.Contains("greenhouse.io", StringComparison.OrdinalIgnoreCase) => "Greenhouse",
        _ when url.Contains("lever.co", StringComparison.OrdinalIgnoreCase) => "Lever",
        _ when url.Contains("ashbyhq.com", StringComparison.OrdinalIgnoreCase) => "Ashby",
        _ => "LinkedIn Easy Apply",
    };

    private static string Slug(string what)
    {
        string slug = NotSlug().Replace(what.ToLowerInvariant(), "-").Trim('-');
        return slug.Length == 0 ? "row" : slug[..Math.Min(40, slug.Length)];
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlug();
}
