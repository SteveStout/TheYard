using System.Text.Json;

namespace JobHunt.Server.Domain.Shared.Slots;

/// <summary>
/// One caught role, as the Function's pounce writes it to nightly/pounce/ on the share when the role passes its gate:
/// the posting's key, company, title and address, the band flag, the instant it went live and the budget flag. A slot
/// run reads it in place of the morning's ranked inputs, so the feed works that one role and nothing else.
/// </summary>
/// <param name="Path">The file's path, relative to the share's root.</param>
/// <param name="Key">jobhunt.Postings.Id, which names the slot.</param>
/// <param name="Company">The company.</param>
/// <param name="Title">The posting's title.</param>
/// <param name="Url">The posting's address.</param>
/// <param name="Band">PASS, FLAG, SKIP or NONE.</param>
/// <param name="LiveAt">When the posting went live.</param>
/// <param name="Budget">True when it went live in the last 21 days of a year not yet ended.</param>
public sealed record RoleHandoff(string Path, long Key, string Company, string Title, string Url, string Band, DateTimeOffset LiveAt, bool Budget)
{
    /// <summary>The hand-off a file holds, or null when it is not one: a hand-off with no key, company or live instant is none.</summary>
    /// <param name="path">The file's path, relative to the share's root.</param>
    /// <param name="json">The file's text.</param>
    public static RoleHandoff? Parse(string path, string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("key", out JsonElement key) || key.ValueKind != JsonValueKind.Number
                || Text(root, "company") is not { Length: > 0 } company
                || !DateTimeOffset.TryParse(Text(root, "live_at"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTimeOffset live))
            {
                return null;
            }
            bool budget = root.TryGetProperty("budget", out JsonElement flag) && flag.ValueKind == JsonValueKind.True;
            return new RoleHandoff(path, key.GetInt64(), company, Text(root, "title"), Text(root, "url"), Text(root, "band"), live, budget);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : string.Empty;
}
