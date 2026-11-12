using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Jobs;

/// <summary>A caught role as the email leg reads its hand-off.</summary>
/// <param name="Key">jobhunt.Postings.Id.</param>
/// <param name="Company">The company.</param>
/// <param name="CompanyKey">Its registrable domain, or null when the scan did not know it.</param>
/// <param name="PassedAt">When the pounce passed it.</param>
public sealed record EmailRole(long Key, string Company, string? CompanyKey, DateTimeOffset PassedAt);

/// <summary>
/// The email leg of a caught role (ADR-118, the campaign sends and the model writes rows), built in shadow: the leader
/// the slot run's feed named is looked up once with the finder on the company's own domain, and an answer is kept only
/// when it is an address on that domain, carries the leader's own name (PounceRules.IsNamedAddress, never a shared box
/// under a leader's name) and cites the https page it was read from. A kept address is written down as a shadow line
/// and nothing is sent: the email ruling in DECISION.md is unanswered and no pounce email template is approved.
/// </summary>
public static partial class PounceEmail
{
    /// <summary>One marker a role, written once the role is settled, so a role is looked up once.</summary>
    public const string Folder = "nightly/pounce/emails";

    /// <summary>One line a settled role.</summary>
    public const string Log = "nightly/pounce/emails.log";

    /// <summary>How long after the pounce passed a role its leader is still looked for.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>One email a company a month: the days a real campaign send keeps the company from another.</summary>
    public const int CompanyDays = 30;

    /// <summary>The role's marker.</summary>
    public static string Marker(long key) => $"{Folder}/{key.ToString(CultureInfo.InvariantCulture)}.md";

    /// <summary>
    /// The finder's month count, the same file the campaign's finder pass keeps (AddressSearch.CountPath), so the free
    /// plan's lookups are counted once for both legs.
    /// </summary>
    public static string CountPath(string finderName, DateTime local) =>
        $"campaign/finder/{finderName}-{local.ToString("yyyy-MM", CultureInfo.InvariantCulture)}.txt";

    /// <summary>The leader a role's queue names: the person in its first block's heading, or null when it names no one.</summary>
    /// <param name="queue">The text of QUEUE-pounce-&lt;day&gt;-&lt;key&gt;.md.</param>
    public static string? Leader(string queue)
    {
        Match first = Heading().Match(queue);
        return first.Success && first.Groups["name"].Value.Trim() is { Length: > 0 } name && name.Contains(' ', StringComparison.Ordinal) ? name : null;
    }

    /// <summary>The hand-off's key, company, domain and pass instant, or null when the file is not a hand-off.</summary>
    public static EmailRole? Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.Number
                || !key.TryGetInt64(out long id)
                || Text(root, "company") is not { Length: > 0 } company
                || !DateTimeOffset.TryParse(Text(root, "passed_at"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var passed))
            {
                return null;
            }
            return new EmailRole(id, company, Text(root, "company_key") is { Length: > 0 } domain ? domain.ToLowerInvariant() : null, passed);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Null when the finder's address may be kept for the leader, or the reason it may not.</summary>
    /// <param name="address">The address the finder gave.</param>
    /// <param name="source">The public page it cited.</param>
    /// <param name="leader">The leader's full name.</param>
    /// <param name="companyKey">The company's registrable domain.</param>
    public static string? Refuse(string address, string source, string leader, string companyKey)
    {
        string to = address.Trim();
        int at = to.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != to.LastIndexOf('@') || to.Any(char.IsWhiteSpace))
        {
            return "not an address";
        }
        if (!string.Equals(CompanyKey.Of(to), companyKey, StringComparison.Ordinal))
        {
            return "not on the company's domain";
        }
        if (!PounceRules.IsNamedAddress(to.ToLowerInvariant(), leader))
        {
            return "not the person's own name";
        }
        return source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !source.Any(char.IsWhiteSpace) ? null : "no https page it was read from";
    }

    /// <summary>The marker's text for a kept address: a shadow line, never a send.</summary>
    public static string Kept(EmailRole role, string leader, string address, string source, DateTimeOffset now) =>
        $"{role.Key} {role.Company} | {leader} | SHADOW: kept {address} ({source}) at {now:O}. Nothing was sent: the email leg waits on the email ruling in J:\\dispatch\\DECISION.md and on a pounce email template Steve has seen.\n";

    /// <summary>The marker's text for a role settled with no address.</summary>
    public static string Settled(EmailRole role, string leader, string reason, DateTimeOffset now) =>
        $"{role.Key} {role.Company} | {leader} | refused: {reason} at {now:O}. Nothing was sent.\n";

    /// <summary>The marker's last line carries the role's log line, so a pass that stopped before the log was written can write it later.</summary>
    public const string LogField = "log: ";

    /// <summary>The log line a settled role's marker carries, or null for a marker that carries none.</summary>
    public static string? MarkerLog(string marker) =>
        marker.Replace("\r", "", StringComparison.Ordinal).Split('\n').LastOrDefault(line => line.StartsWith(LogField, StringComparison.Ordinal)) is { } line
            ? line[LogField.Length..] : null;

    /// <summary>One log line: the Central minute, the role, the leader and what came of it.</summary>
    public static string LogLine(DateTime local, EmailRole role, string leader, string did) =>
        $"{local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} {role.Key} {role.Company} | {leader} | {did}";

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : string.Empty;

    [GeneratedRegex(@"^###\s+1\.\s+(?<name>[^,(\n]+),\s*[^\n]*\(", RegexOptions.Multiline)]
    private static partial Regex Heading();
}
