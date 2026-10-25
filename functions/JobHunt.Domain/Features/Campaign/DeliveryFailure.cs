using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Campaign;

/// <summary>What a delivery failure says: who it failed for, the status, and the reason in a few words.</summary>
public sealed record Bounce(string Recipient, string Status, string Reason);

/// <summary>
/// Gmail's delivery failure (the review, F11), read the way it arrives: from mailer-daemon@googlemail.com, subject
/// "Delivery Status Notification (Failure)", in the same thread as the email that failed, its plain body carrying
/// the heading ("** Address not found **"), Final-Recipient, Status and Diagnostic-Code lines. The thread is the
/// join to EmailsSent; the recipient is the check that the failure is for the address the row sent to.
/// </summary>
public static partial class DeliveryFailure
{
    /// <summary>The Gmail search for the failures of the last two days.</summary>
    public const string Query = "from:mailer-daemon@googlemail.com subject:\"Delivery Status Notification\" newer_than:2d";

    /// <summary>The failure, or null for any other message (a delay notice is not a failure).</summary>
    public static Bounce? Read(InboxMessage message)
    {
        if (!message.Address.StartsWith("mailer-daemon@", StringComparison.Ordinal)
            || !message.Subject.Contains("(Failure)", StringComparison.Ordinal))
        {
            return null;
        }
        string body = message.Lines.Length > 0 ? message.Lines : message.Text;
        string recipient = Recipient().Match(body) is { Success: true } r ? r.Groups[1].Value.Trim().ToLowerInvariant() : "";
        string status = Status().Match(body) is { Success: true } s ? s.Groups[1].Value : "";
        string reason = Heading().Match(body) is { Success: true } h ? h.Groups[1].Value.Trim() : "delivery failed";
        return recipient.Length == 0 ? null : new Bounce(recipient, status, $"{reason}{(status.Length > 0 ? $" ({status})" : "")}");
    }

    [GeneratedRegex(@"Final-Recipient:\s*rfc822;\s*(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex Recipient();

    [GeneratedRegex(@"^Status:\s*([0-9.]+)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex Status();

    [GeneratedRegex(@"\*\*\s*([^*\n]+?)\s*\*\*")]
    private static partial Regex Heading();
}

/// <summary>
/// A reply that asks to stop (the review, F11): the company is marked do-not-email with the date and the reply run
/// drafts nothing for the thread. Matched as phrases in the reply's own text, above any quoted original, case
/// apart; over-matching stops a company, which is the safe direction.
/// </summary>
public static class StopPhrases
{
    /// <summary>The phrases, lower case.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        "not interested", "do not contact", "don't contact", "unsubscribe", "remove me", "no thanks", "no thank you", "stop emailing", "take me off",
    ];

    /// <summary>The first stop phrase in the reply's own lines, or null.</summary>
    public static string? Find(string reply)
    {
        string own = Own(reply).ToLowerInvariant().Replace('’', '\'');
        return All.FirstOrDefault(phrase => own.Contains(phrase, StringComparison.Ordinal));
    }

    /// <summary>The reply above the quoted original: up to the first "On ... wrote:" line or the first line quoted with "&gt;".</summary>
    public static string Own(string reply)
    {
        var kept = new List<string>();
        foreach (string line in (reply ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith('>') || (trimmed.StartsWith("On ", StringComparison.Ordinal) && trimmed.TrimEnd().EndsWith("wrote:", StringComparison.Ordinal)))
            {
                break;
            }
            kept.Add(line);
        }
        return string.Join('\n', kept);
    }
}

/// <summary>
/// Two bounces in one Central day drop the campaign to shadow (the review, F11): the Function cannot change its own
/// settings, so the trip is a row in campaign/switch.log on the share that the send reads first. The newest row
/// wins: "shadow" holds every send whatever EMAIL_CAMPAIGN says, and only a row of "on" written by a person clears it.
/// </summary>
public static class BounceTrip
{
    /// <summary>The bounces in one Central day that trip it.</summary>
    public const int Bounces = 2;

    /// <summary>The share path of the switch's rows.</summary>
    public const string SwitchLog = "campaign/switch.log";

    /// <summary>Whether the newest row of the switch log holds the campaign in shadow.</summary>
    public static bool Tripped(string switchLog) =>
        (switchLog ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() is { } last
        && last.Split(' ', 3) is [_, "shadow", ..];

    /// <summary>The row the trip writes: the Central day, the word, and why.</summary>
    public static string Row(DateOnly centralDay, int bounces) => $"{centralDay:yyyy-MM-dd} shadow {bounces} bounces in one Central day; a person writes \"<date> on <why>\" to clear it";
}
