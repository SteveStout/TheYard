// The box's three routes (ADR: The box reports itself): its agent posts a snapshot once a minute and a live sample
// every two seconds, each with the operator's key in its header, and the card's live panels read the ring back
// anonymously like every other card. The key is checked before the body is read, so a stranger learns nothing from
// a refused body; the rules are the Domain's, asked by the slice's handlers, and a refusal says its one sentence.
using System.Text.Json;
using TheYard.Application.Features.Admin.Cloud;
using TheYard.Data;

namespace TheYard.Api.Features.Admin.Cloud;

// #region box-routes
/// <summary>The box's push and live-read handlers of CloudEndpoints; mapped in CloudEndpoints.cs.</summary>
public static class BoxRoutes
{
    /// <summary>The prefix every box route shares, which the activity ring and the telemetry leave out when a post was accepted.</summary>
    public const string Prefix = RequestRecorder.BoxPrefix;

    /// <summary>The host's wire, snake_case, the names the agent writes.</summary>
    private static readonly JsonSerializerOptions WireFormat = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>Holds one snapshot: 401 without the key, 400 with the sentence that is wrong, 200 with its time.</summary>
    internal static async Task<IResult> SnapshotPost(HttpContext http, AdminKey adminKey, KeepBoxSnapshotHandler keep, CancellationToken cancellation)
    {
        if (!adminKey.IsPresentedBy(http.Request))
        {
            return Refused();
        }
        if (await ReadAsync<BoxSnapshot>(http, cancellation) is not { } snapshot)
        {
            return Results.Problem(detail: "The body is one box snapshot as JSON.", statusCode: 400, title: "The snapshot could not be read");
        }
        var posted = await keep.HandleAsync(new KeepBoxSnapshotCommand(snapshot), cancellation);
        return posted.Wrong is { } wrong ? Results.Problem(detail: wrong, statusCode: 400, title: "The snapshot could not be kept") : Results.Json(new { at = snapshot.At, kept = true });
    }

    /// <summary>Adds one live sample to the ring: 401 without the key, 400 with the sentence that is wrong, 200 with its time.</summary>
    internal static async Task<IResult> LivePost(HttpContext http, AdminKey adminKey, PushBoxSampleHandler push, CancellationToken cancellation)
    {
        if (!adminKey.IsPresentedBy(http.Request))
        {
            return Refused();
        }
        if (await ReadAsync<BoxLiveSample>(http, cancellation) is not { } sample)
        {
            return Results.Problem(detail: "The body is one live sample as JSON.", statusCode: 400, title: "The sample could not be read");
        }
        var posted = await push.HandleAsync(new PushBoxSampleCommand(sample), cancellation);
        return posted.Wrong is { } wrong ? Results.Problem(detail: wrong, statusCode: 400, title: "The sample could not be kept") : Results.Json(new { at = sample.At, kept = true });
    }

    /// <summary>The last minute of live samples and how long the box has been silent, anonymous like every card.</summary>
    internal static async Task<IResult> LiveGet(ReadBoxLiveHandler read, CancellationToken cancellation) =>
        Results.Json(await read.HandleAsync(new ReadBoxLiveQuery(), cancellation), WireFormat);

    /// <summary>The refusal a post without the key gets.</summary>
    private static IResult Refused() =>
        Results.Problem(detail: $"Send the operator's key in {AdminKey.HeaderName}.", statusCode: 401, title: "The key is missing or wrong");

    /// <summary>The body as <typeparamref name="T"/>, or null when it is not one.</summary>
    private static async Task<T?> ReadAsync<T>(HttpContext http, CancellationToken cancellation)
        where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(http.Request.Body, WireFormat, cancellation);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
// #endregion box-routes
