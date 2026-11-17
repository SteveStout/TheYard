using System.Text.RegularExpressions;
using JobHunt.Domain.Shared.Companies;

namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>What a guard reads besides the row: the thread as Gmail has it now, the sends already counted, and BLACKLIST.md.</summary>
/// <param name="Thread">The row's newest thread, read at send time.</param>
/// <param name="Blacklist">BLACKLIST.md's rows.</param>
/// <param name="CompanySends">Reconnects sent to the row's company in the last 60 days, this pass's included.</param>
/// <param name="SentThisPass">Reconnects this pass has already sent.</param>
/// <param name="Now">The pass's clock.</param>
public sealed record ReconnectContext(ThreadSummary Thread, Blacklist Blacklist, int CompanySends, int SentThisPass, DateTimeOffset Now);

/// <summary>
/// The guards before an email reconnect is drafted or sent, in order; the first that holds is the reason logged and
/// nothing is written for the row: a name or company BLACKLIST.md blocks; a former employer; an ATS relay or a
/// no-reply address; a message from the person
/// still unanswered in the thread, which is a reply owed and not a reconnect; a reconnect or a draft to the person in
/// the last 21 days, read from the row and from the thread itself, so one Steve sent by hand counts before any pass has
/// read it; three to the company in 60 days; and fifteen in one pass.
/// </summary>
public static partial class ReconnectGuards
{
    /// <summary>The days after a reconnect, or a draft of one, before the person may get another.</summary>
    public const int RepeatDays = 21;

    /// <summary>The most reconnects to one company in <see cref="CompanyDays"/>.</summary>
    public const int CompanyCap = 3;

    /// <summary>The company cap's window, the application-limits rule's 60 days.</summary>
    public const int CompanyDays = 60;

    /// <summary>The most reconnects one pass sends: DueRule's Monday cap.</summary>
    public const int PassCap = DueRule.MondayCap;

    /// <summary>The reason the row is held, or null when it may go.</summary>
    /// <param name="row">A due email row.</param>
    /// <param name="c">What the guards read.</param>
    public static string? Hold(Conversation row, ReconnectContext c)
    {
        string address = row.Email ?? string.Empty;
        string domain = address.Contains('@', StringComparison.Ordinal) ? address[(address.IndexOf('@', StringComparison.Ordinal) + 1)..] : string.Empty;
        string company = row.Company ?? row.CompanyKey ?? domain;
        if (c.Blacklist.Hit(company, row.FullName) is not null || (row.CompanyKey is { Length: > 0 } key && c.Blacklist.Hit(key) is not null))
        {
            return "blacklist";
        }
        if (FormerEmployers.Of(row.Company, domain) is not null)
        {
            return "former employer";
        }
        if (Relay().IsMatch(address))
        {
            return "relay or no-reply";
        }
        var steve = c.Thread.Messages.Where(m => m.Outbound || HumanSenderRule.IsSteve(m.From)).Select(m => (DateTimeOffset?)m.At).Max();
        if (c.Thread.Messages.Any(m => !m.Outbound && !HumanSenderRule.IsSteve(m.From) && HumanSenderRule.IsHuman(m.From) && (steve is null || m.At > steve)))
        {
            return "reply owed";
        }
        var last = new[] { steve, row.DraftedAt }.Max();
        if (last is { } at && c.Now < at.AddDays(RepeatDays))
        {
            return "within 21 days";
        }
        if (c.CompanySends >= CompanyCap)
        {
            return "company cap";
        }
        return c.SentThisPass >= PassCap ? "pass cap" : null;
    }

    [GeneratedRegex(@"(^|[@.+_-])(no-?reply|do-?not-?reply|lever|greenhouse|ashby(hq)?|workday|myworkday|icims|jobvite)([@.+_-]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex Relay();
}
