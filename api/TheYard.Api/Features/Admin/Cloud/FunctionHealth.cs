// The Function's health route as the site reads it (MergerWatch.cs): the switches the nightly card shows, and the two
// no-email-day fields the campaign card holds on. The fields are the mail gate's own reading of
// dispatch/NO-EMAIL-DAYS.txt, answered by the Function; the site keeps no copy of the dates and parses none.
using System.Globalization;
using System.Text.Json;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>One answer of the Function's health route.</summary>
/// <param name="Switches">The processes' switches by name; null when the answer carried none.</param>
/// <param name="NoEmailToday">"true" when the gate's file lists today, "false" when it does not; null when the file did not read or the route did not say.</param>
/// <param name="NextEmailDay">The day of the next campaign send the gate lets through, yyyy-MM-dd; null when the route did not say.</param>
public sealed record FunctionHealth(IReadOnlyDictionary<string, string>? Switches, string? NoEmailToday, string? NextEmailDay)
{
    /// <summary>The answer's fields, each kept only in its own shape: a JSON true or false, and a yyyy-MM-dd day.</summary>
    /// <param name="answer">The health route's JSON.</param>
    public static FunctionHealth From(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object)
        {
            return new FunctionHealth(null, null, null);
        }
        string? today = answer.TryGetProperty("no_email_today", out var listed) && listed.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? (listed.ValueKind == JsonValueKind.True ? "true" : "false")
            : null;
        string? next = answer.TryGetProperty("next_email_day", out var day) && day.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(day.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
        return new FunctionHealth(NightlyProcesses.Switches(answer), today, next);
    }
}
