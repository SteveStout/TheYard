using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobHunt.Server.Domain.Shared.Results;

/// <summary>What the model says happened to one worklist row, with the evidence for it.</summary>
/// <param name="Row">The worklist row's number.</param>
/// <param name="Outcome">sent, skipped, blocked or failed.</param>
/// <param name="Reason">Why, for anything but sent.</param>
/// <param name="Person">The person, filled in when the lane found the name.</param>
/// <param name="Title">The person's title as read.</param>
/// <param name="ProfileUrl">The profile or posting the lane worked.</param>
/// <param name="Evidence">The verifications.</param>
public sealed record RowResult(
    [property: JsonPropertyName("row")] int Row,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("person")] string? Person,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("profile_url")] string? ProfileUrl,
    [property: JsonPropertyName("evidence")] RowEvidence? Evidence);

/// <summary>The evidence a send carries. Each action fills its own fields.</summary>
/// <param name="Pending">An invite: the button read Pending.</param>
/// <param name="Toast">An invite: the toast's words.</param>
/// <param name="ReadBack">The note as read off the screenshot, or the sent bubble's body.</param>
/// <param name="Confirmation">An application: the confirmation page's words and URL.</param>
/// <param name="Screenshot">The saved screenshot's path.</param>
public sealed record RowEvidence(
    [property: JsonPropertyName("pending")] bool? Pending,
    [property: JsonPropertyName("toast")] string? Toast,
    [property: JsonPropertyName("read_back")] string? ReadBack,
    [property: JsonPropertyName("confirmation")] string? Confirmation,
    [property: JsonPropertyName("screenshot")] string? Screenshot);

/// <summary>A lane's structured result, in the shape <see cref="LaneResultSchema"/> asks for.</summary>
/// <param name="Rows">One entry per worklist row.</param>
/// <param name="LiveInviteCount">The count read on the sent page, or null where the lane read none.</param>
/// <param name="StopCondition">none, weekly_limit, checkpoint, captcha, sign_in, restriction or browser_down.</param>
/// <param name="StopDetail">What the page said.</param>
/// <param name="StopScreenshot">The saved screenshot of it.</param>
public sealed record LaneResult(
    [property: JsonPropertyName("rows")] IReadOnlyList<RowResult> Rows,
    [property: JsonPropertyName("live_invite_count")] int? LiveInviteCount,
    [property: JsonPropertyName("stop_condition")] string StopCondition,
    [property: JsonPropertyName("stop_detail")] string? StopDetail,
    [property: JsonPropertyName("stop_screenshot")] string? StopScreenshot)
{
    /// <summary>True when a stop condition fired that ends both LinkedIn lanes for the day.</summary>
    public bool StopsLinkedIn => StopCondition is "weekly_limit" or "checkpoint" or "captcha" or "sign_in" or "restriction";

    /// <summary>The result out of its JSON, or null when it does not parse.</summary>
    /// <param name="json">The structured_output object.</param>
    public static LaneResult? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<LaneResult>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>The JSON schema every lane's run is held to, passed to the CLI as --json-schema.</summary>
public static class LaneResultSchema
{
    /// <summary>The schema.</summary>
    public const string Json = """
        {"type":"object","required":["rows","live_invite_count","stop_condition","stop_detail","stop_screenshot"],"additionalProperties":false,
        "properties":{
        "rows":{"type":"array","items":{"type":"object","required":["row","outcome","reason","person","title","profile_url","evidence"],"additionalProperties":false,
        "properties":{"row":{"type":"integer"},"outcome":{"type":"string","enum":["sent","skipped","blocked","failed"]},"reason":{"type":["string","null"]},
        "person":{"type":["string","null"]},"title":{"type":["string","null"]},"profile_url":{"type":["string","null"]},
        "evidence":{"type":["object","null"],"required":["pending","toast","read_back","confirmation","screenshot"],"additionalProperties":false,
        "properties":{"pending":{"type":["boolean","null"]},"toast":{"type":["string","null"]},"read_back":{"type":["string","null"]},"confirmation":{"type":["string","null"]},"screenshot":{"type":["string","null"]}}}}}},
        "live_invite_count":{"type":["integer","null"]},
        "stop_condition":{"type":"string","enum":["none","weekly_limit","checkpoint","captcha","sign_in","restriction","browser_down"]},
        "stop_detail":{"type":["string","null"]},"stop_screenshot":{"type":["string","null"]}}}
        """;
}
