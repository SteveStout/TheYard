// The nightly card's push route (ADR: The Nightly Function on the Admin tab): the workflow that
// ran posts the run it was, with the operator's key in its header, the way the Claude usage card is
// already fed (ADR: Every Claude lane reports its usage). The body is
// read only after the key is checked, so a stranger learns nothing from a refused body.
//
// What the wire carries is GitHub's own facts for the run, not words about them: the id, the run
// number, the event, the display title, when the run was created, how long it had taken by the time
// it posted, and what the job had come to at that step. The route turns those into a NightlyRun
// through the very rules GitHubRuns.Parse uses on GitHub's list, so a run pushed and the same run
// read from GitHub are one document, field for field, and the slot and the lane cannot drift between
// the two. The run's own rules are the Domain's (NightlyRunRules.Validate), asked by the slice's
// handler, and the route answers its one sentence when one is broken.
using System.Globalization;
using System.Text.Json;
using TheYard.Application.Features.Admin.Cloud;
using TheYard.Data;

namespace TheYard.Api.Features.Admin.Cloud;

// #region nightly-run-routes
/// <summary>The nightly run's push handler of CloudEndpoints; mapped in CloudEndpoints.cs.</summary>
public static class NightlyRunRoutes
{
    /// <summary>The host's wire, snake_case, the names a workflow writes in its JSON.</summary>
    private static readonly JsonSerializerOptions WireFormat = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>The title of a refusal the rules made, which is the same whichever rule made it.</summary>
    private const string Unkeepable = "The run could not be kept";

    /// <summary>One run of the nightly workflow as the workflow itself posts it, under the wire's snake_case names.</summary>
    /// <param name="Run">GitHub's id for the run, which is also its document's id.</param>
    /// <param name="Number">The run's number in the workflow, as GitHub numbers it.</param>
    /// <param name="Event">GitHub's event: repository_dispatch or workflow_dispatch.</param>
    /// <param name="Title">GitHub's display title for the run, which is the workflow's run name.</param>
    /// <param name="At">When GitHub created the run, ISO 8601.</param>
    /// <param name="TookSeconds">How long it had run by the time it posted itself; null when the step does not know.</param>
    /// <param name="CameTo">What the job had come to at the posting step; GitHub's own conclusion can still differ, and the reader reconciles it.</param>
    public sealed record NightlyRunWire(string? Run, long Number, string? Event, string? Title, DateTimeOffset? At, long? TookSeconds, string? CameTo);

    /// <summary>
    /// The posted run as a <see cref="NightlyRun"/>, or the one sentence that stops it being one
    /// before the Domain's rules are asked. The slot, who started it, the lane and whether it was a
    /// dry run are not taken from the wire: they are read off the event and the title by the same
    /// rules GitHub's list is read by, so the two copies of a run cannot say different things.
    /// </summary>
    /// <param name="wire">The posted body.</param>
    public static (NightlyRun? Run, string? Wrong) Read(NightlyRunWire wire)
    {
        if (wire.Event is not (GitHubRuns.Dispatched or GitHubRuns.ByHandEvent))
        {
            return (null, $"event is {GitHubRuns.Dispatched} or {GitHubRuns.ByHandEvent}.");
        }
        if (wire.At is not { } at)
        {
            return (null, "at is an ISO 8601 instant.");
        }
        var (lane, dry) = GitHubRuns.LaneOf(wire.Event, wire.Title ?? "");
        return (new NightlyRun(
            wire.Run ?? "",
            wire.Number,
            at,
            NightlySlots.Local(at).ToString("HH:mm", CultureInfo.InvariantCulture),
            wire.Event == GitHubRuns.Dispatched ? GitHubRuns.ByFunction : GitHubRuns.ByHand,
            dry,
            lane,
            wire.TookSeconds,
            wire.CameTo ?? ""), null);
    }

    /// <summary>
    /// Keeps one run of the nightly workflow: 401 without the key, 400 with the sentence that is
    /// wrong, 503 with the log's reason when it cannot keep runs now, 200 with the id the run is kept
    /// under. Posting the same run again replaces the copy kept.
    /// </summary>
    internal static async Task<IResult> RunPost(HttpContext http, AdminKey adminKey, KeepNightlyRunHandler keep, CancellationToken cancellation)
    {
        if (!adminKey.IsPresentedBy(http.Request))
        {
            return Results.Problem(detail: $"Send the operator's key in {AdminKey.HeaderName}.", statusCode: 401, title: "The key is missing or wrong");
        }
        NightlyRunWire? wire;
        try
        {
            wire = await JsonSerializer.DeserializeAsync<NightlyRunWire>(http.Request.Body, WireFormat, cancellation);
        }
        catch (JsonException)
        {
            wire = null;
        }
        if (wire is null)
        {
            return Results.Problem(detail: "The body is one nightly run as JSON.", statusCode: 400, title: "The run could not be read");
        }
        var (run, wrong) = Read(wire);
        if (wrong is not null)
        {
            return Results.Problem(detail: wrong, statusCode: 400, title: Unkeepable);
        }
        var kept = await keep.HandleAsync(new KeepNightlyRunCommand(run!), cancellation);
        return kept.Wrong is { } sentence ? Results.Problem(detail: sentence, statusCode: 400, title: Unkeepable)
            : kept.Unavailable is { } reason ? Results.Problem(detail: reason, statusCode: 503, title: "The run log cannot keep runs now")
            : Results.Json(new { run = run!.Id, slot = run.Slot, lane = run.Lane });
    }
}
// #endregion nightly-run-routes
