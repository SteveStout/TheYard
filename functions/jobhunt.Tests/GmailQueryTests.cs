using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// Every search the Function sends, held character for character to the string it sent at 1.0.4.66, when the
/// use cases still wrote Gmail's syntax themselves. The search is data now (MailSearch) and only the Gmail
/// adapter writes the syntax (GmailQuery); this is the proof that moving it changed nothing Gmail sees.
/// </summary>
public sealed class GmailQueryTests
{
    [Fact]
    public void Every_search_the_Function_sends_is_the_string_it_sent_before()
    {
        var sent = new Dictionary<string, MailSearch>(StringComparer.Ordinal)
        {
            ["in:inbox from:(greenhouse.io OR greenhouse-mail.io OR lever.co OR ashbyhq.com OR myworkday.com OR workday.com OR icims.com OR jobvite.com OR smartrecruiters.com OR taleo.net OR successfactors.com OR bamboohr.com OR workable.com OR workablemail.com)"] = AtsSweep.Search,
            ["in:inbox from:jobalerts-noreply@linkedin.com"] = LeadsHarvest.SearchFor(new LinkedInDigest()),
            ["in:inbox from:jobs-noreply@linkedin.com (subject:\"is hiring for\" OR subject:\"apply now to\")"] = LeadsHarvest.SearchFor(new LinkedInNudge()),
            ["in:inbox from:jobs@my.theladders.com"] = LeadsHarvest.SearchFor(new LaddersAlert()),
            ["in:inbox from:noreply.jobs2web.com"] = LeadsHarvest.SearchFor(new Jobs2WebAlert()),
            ["from:mailer-daemon@googlemail.com subject:\"Delivery Status Notification\" newer_than:2d"] = CampaignInbox.Bounces,
            ["from:pat@acme.com newer_than:15d"] = CampaignInbox.RepliesFrom("pat@acme.com"),
            ["in:inbox"] = InboxTriage.Search,
            ["in:inbox newer_than:2d -from:me"] = HumanMail.Search,
        };
        foreach (var (before, search) in sent)
        {
            Assert.Equal(before, GmailQuery.Compose(search));
        }
        Assert.Equal(BoardAlerts.All.Count, sent.Keys.Count(key => key.StartsWith("in:inbox from:", StringComparison.Ordinal)) - 1);
    }
}
