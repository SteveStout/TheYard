// The Admin tab's usage route (ADR: Every Claude lane reports its usage): a lane posts one run's
// usage document with the operator's key in its header. The body is read only after the key is
// checked, so a stranger learns nothing from a refused body; the document's rules are the Domain's
// (UsageRecord.Validate), asked by the slice's handler, and the route answers its one sentence when one is broken.
using System.Text.Json;
using TheYard.Application.Features.Usage;

namespace TheYard.Api.Features.Admin.Operations;

/// <summary>The usage handler of AdminEndpoints; the type and what it is for are described in AdminEndpoints.cs.</summary>
public static partial class AdminEndpoints
{
    /// <summary>The usage document as a lane posts it, under the wire's snake_case names.</summary>
    /// <param name="Lane">The lane's name.</param>
    /// <param name="Kind">plan or api.</param>
    /// <param name="Model">The model id.</param>
    /// <param name="TokensIn">Input tokens not read from the cache.</param>
    /// <param name="CacheWrite">Input tokens written to the cache.</param>
    /// <param name="CacheRead">Input tokens read from the cache.</param>
    /// <param name="TokensOut">Output tokens.</param>
    /// <param name="WallMs">How long the run took.</param>
    /// <param name="CostUsd">The API-equivalent cost; null when the lane does not know.</param>
    /// <param name="Run">The run's id.</param>
    /// <param name="Slot">HH:MM Central, or null.</param>
    /// <param name="Mode">What the run did, or null.</param>
    /// <param name="At">When the run started, ISO 8601.</param>
    public sealed record UsageWire(string? Lane, string? Kind, string? Model, long TokensIn, long CacheWrite, long CacheRead, long TokensOut, long WallMs, decimal? CostUsd, string? Run, string? Slot, string? Mode, DateTimeOffset? At);

    /// <summary>The document as a UsageRecord, or the one sentence that stops it being one before the Domain's rules are asked.</summary>
    /// <param name="wire">The posted body.</param>
    public static (UsageRecord? Record, string? Wrong) Read(UsageWire wire) =>
        wire.Kind is not ("plan" or "api") ? (null, "kind is plan or api.")
        : wire.At is not { } at ? (null, "at is an ISO 8601 instant.")
        : (new UsageRecord(wire.Lane ?? "", wire.Kind == "api" ? UsageKind.Api : UsageKind.Plan, wire.Model ?? "", wire.TokensIn, wire.CacheWrite, wire.CacheRead, wire.TokensOut, wire.WallMs, wire.CostUsd, wire.Run ?? "", wire.Slot, wire.Mode, at), null);

    /// <summary>
    /// Keeps one run's usage document: 401 without the key, 400 with the sentence that is wrong, 503 with
    /// the log's reason when it cannot keep documents now, 200 with the key it is kept under. Posting the
    /// same lane and run again replaces the copy kept.
    /// </summary>
    private static async Task<IResult> UsagePost(HttpContext http, AdminKey adminKey, KeepUsageHandler keep, CancellationToken cancellation)
    {
        if (!adminKey.IsPresentedBy(http.Request))
        {
            return Results.Problem(detail: $"Send the operator's key in {AdminKey.HeaderName}.", statusCode: 401, title: "The key is missing or wrong");
        }
        UsageWire? wire;
        try
        {
            wire = await JsonSerializer.DeserializeAsync<UsageWire>(http.Request.Body, WireFormat, cancellation);
        }
        catch (JsonException)
        {
            wire = null;
        }
        if (wire is null)
        {
            return Results.Problem(detail: "The body is one usage document as JSON.", statusCode: 400, title: "The usage document could not be read");
        }
        var (record, wrong) = Read(wire);
        if (wrong is not null)
        {
            return Results.Problem(detail: wrong, statusCode: 400, title: "The usage document could not be kept");
        }
        var kept = await keep.HandleAsync(new KeepUsageCommand(record!), cancellation);
        return kept.Wrong is { } sentence ? Results.Problem(detail: sentence, statusCode: 400, title: "The usage document could not be kept")
            : kept.Unavailable is { } reason ? Results.Problem(detail: reason, statusCode: 503, title: "The usage log cannot keep documents now")
            : Results.Json(new { key = MemoryUsageLog.KeyOf(record!) });
    }
}
