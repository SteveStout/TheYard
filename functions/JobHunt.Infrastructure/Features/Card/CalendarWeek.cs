using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobHunt.Infrastructure.Features.Card;

/// <summary>
/// The next seven days of Steve's calendar as the card needs them (the automation plan's fifth
/// increment): each event that reads as an interview or a call, with how he joins it. A call with no
/// join link and no number is the one failure this card treats as major, so its line starts with STOP:
/// a booked call is the only item here that cannot be recovered later, because the hour passes whether
/// or not anybody found the way in.
/// Read-only: the calendar is read with the calendar.readonly scope and nothing is written to it.
/// </summary>
public static partial class CalendarWeek
{
    /// <summary>
    /// The calls in the days ahead of now as card lines, each naming Google Calendar, or one line saying why they were
    /// not read: no Google settings, no calendar scope on the token, or the error's type.
    /// </summary>
    /// <param name="http">The client factory.</param>
    /// <param name="now">From when.</param>
    /// <param name="days">How many days ahead.</param>
    /// <param name="none">The line when the calendar holds no call.</param>
    /// <param name="ct">Cancels the read.</param>
    public static async Task<IReadOnlyList<string>> ReadAsync(IHttpClientFactory http, DateTimeOffset now, int days, string none, CancellationToken ct)
    {
        if (GoogleCredentials.FromEnvironment() is not { } google)
        {
            return ["Interviews: not read, the Function holds no Google settings"];
        }
        try
        {
            using var client = await google.ClientAsync(http, ct);
            string from = Uri.EscapeDataString(now.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            string to = Uri.EscapeDataString(now.AddDays(days).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            using var answer = await client.GetAsync($"https://www.googleapis.com/calendar/v3/calendars/primary/events?singleEvents=true&orderBy=startTime&timeMin={from}&timeMax={to}&maxResults=100", ct);
            if (answer.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
            {
                return ["Interviews: not read, the token holds no calendar scope yet (one consent click, on the AUTOMATE card)"];
            }
            answer.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await answer.Content.ReadAsStringAsync(ct));
            var lines = Lines(json.RootElement);
            return lines.Count == 0 ? [none] : [.. lines.Select(line => line + " (Google Calendar)")];
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return [$"Interviews: not read ({ex.GetType().Name})"];
        }
    }

    /// <summary>The card's lines for the events Google Calendar answered, soonest first.</summary>
    /// <param name="events">The events.list answer (singleEvents, ordered by start time).</param>
    public static IReadOnlyList<string> Lines(JsonElement events)
    {
        var lines = new List<string>();
        if (!events.TryGetProperty("items", out var items))
        {
            return lines;
        }
        foreach (var item in items.EnumerateArray())
        {
            string summary = Text(item, "summary");
            string description = Text(item, "description");
            if (!Call().IsMatch(summary + " " + description) || !item.TryGetProperty("start", out var start) || !start.TryGetProperty("dateTime", out var when))
            {
                continue;
            }
            var at = CentralClock.Local(DateTimeOffset.Parse(when.GetString()!, CultureInfo.InvariantCulture));
            string? how = How(item, description);
            string day = at.ToString("ddd M/d h:mm tt", CultureInfo.InvariantCulture) + " Central";
            lines.Add(how is null
                ? $"STOP: {day}, {summary.Trim()}: no join link or number on the event"
                : $"{day}, {summary.Trim()}: {how}");
        }
        return lines;
    }

    /// <summary>How he joins: the Meet link, a conference entry point, or a link or number in the location or the description.</summary>
    public static string? How(JsonElement item, string description)
    {
        if (Text(item, "hangoutLink") is { Length: > 0 } meet)
        {
            return "Google Meet " + meet;
        }
        if (item.TryGetProperty("conferenceData", out var conference) && conference.TryGetProperty("entryPoints", out var points))
        {
            foreach (var point in points.EnumerateArray())
            {
                if (Text(point, "uri") is { Length: > 0 } uri)
                {
                    return uri;
                }
            }
        }
        string place = Text(item, "location") + " " + description;
        var link = JoinLink().Match(place);
        if (link.Success)
        {
            return link.Value;
        }
        var phone = Phone().Match(place);
        return phone.Success ? "phone " + phone.Value.Trim() : null;
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    [GeneratedRegex(@"\b(interview|screen(ing)?|recruiter|hiring|technical|panel|onsite|phone call|video call|intro call|call with|chat with)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Call();

    [GeneratedRegex(@"https://[^\s""<>]*(zoom\.us|teams\.microsoft\.com|teams\.live\.com|meet\.google\.com|webex\.com|gotomeet|chime\.aws|whereby\.com|bluejeans\.com)[^\s""<>]*", RegexOptions.IgnoreCase)]
    private static partial Regex JoinLink();

    [GeneratedRegex(@"(\+?1[\s.-]?)?\(?\d{3}\)?[\s.-]\d{3}[\s.-]\d{4}")]
    private static partial Regex Phone();
}
