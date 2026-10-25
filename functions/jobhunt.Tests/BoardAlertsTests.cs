using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// Every board the leads harvest reads, on the shapes those boards sent on 22 and 23 October 2026 (the
/// SuccessFactors agent's HTML as sent, cut to two jobs; Ladders' text the way GmailInbox.Text leaves it
/// once the markup is gone; LinkedIn's two nudge subjects), and the band floor the filter adds.
/// </summary>
public sealed class BoardAlertsTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 23, 9, 25, 35, TimeSpan.Zero);

    private const string AgentHtml = """
        <td>The following jobs matched your search agent at Olympus Corporation of the Americas and can be found at <a class="agentsitelink" href="http://careers.olympusamerica.com/?from=email">careers.olympusamerica.com</a>.
        <br/><strong>Job Matches:</strong>
        <br/><span class="agentjoblink_bullet"></span> <a class="agentjoblink" href="http://careers.olympusamerica.com/job/Westborough-Sr_-Engineer%2C-Sustaining-R&amp;D-MA-01581/1403043700/?from=email&refid=22038790500&utm_source=J2WEmail">Sr. Engineer, Sustaining R&amp;D - Westborough, MA, US, 01581</a><br/>
        <span class="agentjoblink_bullet"></span> <a class="agentjoblink" href="http://careers.olympusamerica.com/job/Westborough-Lead%2C-Data-Governance-&amp;-Management-MA-01581/1431214400/?from=email&refid=22038790500">Lead, Data Governance &amp; Management - Westborough, MA, US, 01581</a><br/>
        </td>
        """;

    private const string LaddersText =
        "Discover new jobs first and apply today! Jobs Posted in the Last 24 Hours Senior Software Engineer, Full-Stack $160K - $210K* | Epic | Remote Apply Now Apply Now " +
        "Web Technical Lead $120K - $145K* | RIVA Solutions Inc. | Remote Apply Now Apply Now " +
        "Lead Front-End Engineer (React, TypeScript and MUI) $120K - $140K* | DecisionPoint | Cortek | Remote Apply Now Apply Now " +
        "*Ladders estimates are based on our calculations. Do these jobs match what you're looking for?";

    [Fact]
    public void A_SuccessFactors_agent_reads_each_job_from_its_link_with_the_tracking_cut()
    {
        var mail = new InboxMessage("j1", "t1", "olympuscorp1-jobnotification@noreply.jobs2web.com", "New jobs posted from careers.olympusamerica.com", At, "") { Html = AgentHtml };
        var board = new Jobs2WebAlert();

        var (leads, unread) = board.Read(mail);

        Assert.True(board.Sends(mail.Address));
        Assert.Equal(0, unread);
        Assert.Equal(2, leads.Count);
        Assert.Equal("Olympus Corporation of the Americas", leads[0].Company);
        Assert.Equal("Sr. Engineer, Sustaining R&D", leads[0].Title);
        Assert.Equal("Westborough, MA, US, 01581", leads[0].Location);
        Assert.Equal("http://careers.olympusamerica.com/job/Westborough-Sr_-Engineer%2C-Sustaining-R&D-MA-01581/1403043700/", leads[0].Link);
        Assert.DoesNotContain("refid", leads[1].Link, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Ladders_alert_reads_title_band_company_and_place_and_keeps_a_company_named_in_two_parts()
    {
        var mail = new InboxMessage("l1", "t2", "Ladders <jobs@my.theladders.com>", "New Jobs Posted: Apply before others do", At, LaddersText);

        var (leads, unread) = new LaddersAlert().Read(mail);

        Assert.Equal(0, unread);
        Assert.Equal(3, leads.Count);
        Assert.Equal(("Epic", "Senior Software Engineer, Full-Stack", "Remote", "$160K - $210K"), (leads[0].Company, leads[0].Title, leads[0].Location, leads[0].Band));
        Assert.Equal("Web Technical Lead", leads[1].Title);
        Assert.Equal("DecisionPoint / Cortek", leads[2].Company);
        Assert.StartsWith("https://www.theladders.com/jobs/searchresults-jobs?keywords=", leads[0].Link, StringComparison.Ordinal);
        Assert.DoesNotContain("t.ladders.co", string.Join(" ", leads.Select(l => l.Link)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_Ladders_alert_with_no_job_in_it_is_unread_and_stays()
    {
        var (leads, unread) = new LaddersAlert().Read(new InboxMessage("l2", "t3", "jobs@my.theladders.com", "Your profile", At, "Update your job preferences to get more accurate matches."));
        Assert.Empty(leads);
        Assert.Equal(1, unread);
    }

    [Theory]
    [InlineData("Steven, apply now to ‘Senior Staff Software Engineer at ServiceTitan’", "ServiceTitan", "Senior Staff Software Engineer")]
    [InlineData("CSI Companies is hiring for a API role", "CSI Companies", "API role")]
    public void A_LinkedIn_nudge_reads_its_job_from_the_subject_and_the_first_job_link(string subject, string company, string title)
    {
        var mail = new InboxMessage("n1", "t4", "LinkedIn <jobs-noreply@linkedin.com>", subject, At, "Remote. Apply now")
        {
            Lines = "View job: https://www.linkedin.com/comm/jobs/view/4123456789/?trackingId=x",
        };

        var (leads, unread) = new LinkedInNudge().Read(mail);

        Assert.Equal(0, unread);
        var lead = Assert.Single(leads);
        Assert.Equal((company, title, "https://www.linkedin.com/jobs/view/4123456789/"), (lead.Company, lead.Title, lead.Link));
        Assert.StartsWith("Remote", lead.Location, StringComparison.Ordinal);
    }

    [Fact]
    public void A_LinkedIn_mail_that_is_not_a_nudge_is_not_read_as_one()
    {
        var (leads, unread) = new LinkedInNudge().Read(new InboxMessage("n2", "t5", "jobs-noreply@linkedin.com", "Your application was viewed by LINQ", At, "x"));
        Assert.Empty(leads);
        Assert.Equal(1, unread);
        Assert.Contains("is hiring for", new LinkedInNudge().SubjectPhrases);
    }

    [Fact]
    public void A_band_printed_under_the_floor_fails_and_one_over_it_or_none_passes()
    {
        var under = new Lead("RIVA Solutions Inc.", "Web Technical Lead", "Remote", "https://x/1", "l1") { Band = "$120K - $145K" };
        var over = new Lead("Epic", "Senior Software Engineer", "Remote", "https://x/2", "l1") { Band = "$160K - $210K" };
        var none = new Lead("Acme", "Principal Engineer", "Remote", "https://x/3", "l1");

        var judged = LeadsFilter.Judge([under, over, none], [], [], new DateOnly(2026, 10, 23)).ToDictionary(j => j.Lead.Link, j => j.Failed);

        Assert.Equal("under the floor: the band tops at $145K", judged["https://x/1"]);
        Assert.Null(judged["https://x/2"]);
        Assert.Null(judged["https://x/3"]);
        Assert.Equal(210, BoardAlerts.TopOfBand("$160K - $210K*"));
        Assert.Null(BoardAlerts.TopOfBand(null));
    }

    [Fact]
    public void The_harvest_reads_four_boards_and_every_reader_names_its_own_sender()
    {
        Assert.Equal(new[] { "LinkedIn digest", "LinkedIn nudge", "Ladders", "SuccessFactors agent" }, BoardAlerts.All.Select(b => b.Board));
        Assert.All(BoardAlerts.All, board => Assert.NotEmpty(board.Senders));
        Assert.True(new LaddersAlert().Sends(Mail.Address("Ladders <jobs@my.theladders.com>")));
        Assert.False(new LaddersAlert().Sends("dan@technologypartners.net"));
    }
}
