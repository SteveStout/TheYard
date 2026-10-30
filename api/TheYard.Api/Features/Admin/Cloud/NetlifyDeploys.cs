// What the edge's deploy list says, in the card's words: whether a deploy went live, was skipped
// because nothing under edge/ changed, or failed, and the log and table rows the card prints from
// the newest few. Its own file because reading a list Netlify already answered with is a different
// job from asking Netlify anything, which is NetlifyEdge.cs: this side changes when the card's
// words or Netlify's states change, and nothing here opens a socket.
using System.Globalization;
using System.Text.Json;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>
/// The deploys Netlify lists, read for the edge's card (ADR: One frame for every resource, the
/// addendum on the edge). Every member is pure: it is handed the JSON the reader already has.
/// </summary>
public static class NetlifyDeploys
{
    /// <summary>
    /// What a deploy came to, in the card's words: live when Netlify published it, skipped when the
    /// repository's ignore rule found no change under edge/ (Netlify files those as errors with the
    /// reason "Canceled build due to no content change"), failed for any other error, and building
    /// while it runs. The card never calls a skipped build failed.
    /// </summary>
    public static string Outcome(JsonElement deploy)
    {
        string? state = AzureMetrics.Text(deploy, "state");
        string reason = AzureMetrics.Text(deploy, "error_message") ?? "";
        return state switch
        {
            "ready" => "live",
            "skipped" => "skipped",
            "error" when reason.Contains("no content change", StringComparison.OrdinalIgnoreCase) || reason.Contains("ignore", StringComparison.OrdinalIgnoreCase) => "skipped",
            "error" or "rejected" => "failed",
            _ => "building",
        };
    }

    /// <summary>The deploy log the card prints: the newest few, each with its time and what it came to.</summary>
    public static (string Log, int Live, int Skipped, int Failed) DeployLog(JsonElement deploys, int newest = 5)
    {
        var listed = deploys.ValueKind == JsonValueKind.Array ? deploys.EnumerateArray().ToList() : [];
        var outcomes = listed.Select(d => (At: AzureMetrics.Text(d, "created_at"), Outcome: Outcome(d))).ToList();
        string log = string.Join(" · ", outcomes.Take(newest).Select(o =>
            (DateTimeOffset.TryParse(o.At, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at.UtcDateTime.ToString("MMM d HH:mm", CultureInfo.InvariantCulture) + " UTC " : "")
            + (o.Outcome == "skipped" ? "skipped, no edge change" : o.Outcome)));
        return (outcomes.Count == 0 ? "no deploys listed" : log, outcomes.Count(o => o.Outcome == "live"), outcomes.Count(o => o.Outcome == "skipped"), outcomes.Count(o => o.Outcome == "failed"));
    }

    /// <summary>The newest deploys as table rows: when, what kind of thing, and what it came to.</summary>
    public static string DeployRows(JsonElement deploys, int newest = 8) =>
        CloudTable.ToFact((deploys.ValueKind == JsonValueKind.Array ? deploys.EnumerateArray().ToList() : []).Take(newest).Select(d => new[]
        {
            DateTimeOffset.TryParse(AzureMetrics.Text(d, "created_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at.UtcDateTime.ToString("MMM d HH:mm", CultureInfo.InvariantCulture) + " UTC" : "",
            AzureMetrics.Text(d, "context") == "production" ? "Production deploy" : "Deploy",
            Outcome(d) == "skipped" ? "skipped, no edge change" : Outcome(d),
        }));
}
