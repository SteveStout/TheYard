using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>
/// Whether a person wrote a message: not Steve, not a machine (Mail.IsMachine and the mail system's own senders
/// below), and not a former employer's work address, which the job hunt never writes to. A former colleague's
/// personal address is a person like any other.
/// </summary>
public static partial class HumanSenderRule
{
    /// <summary>The connected account (ADR-JOBHUNT-001): a message from it is Steve's, never a person's.</summary>
    public const string Steve = "stevenstout11@gmail.com";

    /// <summary>The mail system's own senders that Mail.IsMachine does not name.</summary>
    public static readonly IReadOnlyList<string> SystemAddresses = ["messages-noreply@linkedin.com", "calendar-notification@google.com"];

    /// <summary>True when the address is Steve's own.</summary>
    public static bool IsSteve(string address) => string.Equals(Mail.Address(address), Steve, StringComparison.Ordinal);

    /// <summary>True when a person sent from the address.</summary>
    public static bool IsHuman(string address)
    {
        string a = Mail.Address(address);
        if (a.Length == 0 || !a.Contains('@', StringComparison.Ordinal) || a == Steve || Mail.IsMachine(a) || SystemAddresses.Contains(a))
        {
            return false;
        }
        string local = a[..a.IndexOf('@', StringComparison.Ordinal)];
        string domain = Mail.Domain(a);
        if (SystemLocal().IsMatch(local) || domain.StartsWith("bounce.", StringComparison.Ordinal) || domain.Contains(".bounce.", StringComparison.Ordinal))
        {
            return false;
        }
        return FormerEmployers.Of(null, domain) is null;
    }

    /// <summary>True when the address is the mail system answering for a message that did not arrive.</summary>
    public static bool IsBounce(string address)
    {
        string a = Mail.Address(address);
        int at = a.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && a[..at] is "mailer-daemon" or "postmaster";
    }

    [GeneratedRegex(@"^(mailer-daemon|postmaster|calendar-notification|invitations?|noreply-.*|bounces?(\+.*)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex SystemLocal();
}
