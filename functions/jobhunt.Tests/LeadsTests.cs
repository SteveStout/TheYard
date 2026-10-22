using JobHunt.Functions.Leads;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The leads harvest's two pure parts: a LinkedIn job alert's requisitions read from its text with the
/// tracking cut from the link, and the rules a lead passes (remote, not applied to, clear of RULE 50).
/// The alert below is in the shape LinkedIn sends, with made-up jobs.
/// </summary>
public sealed class LeadsTests
{
    private const string Alert = """
        Jobs similar to your alert

        There were no results for your alert, but these similar jobs may be a fit. Manage alert:
        https://www.linkedin.com/comm/jobs/alerts/?lipi=x&trk=y

        Principal Engineer (.NET)
        Acme
        United States (Remote)
        Top applicant
        View job: https://www.linkedin.com/comm/jobs/view/4000000001/?trackingId=abc&refId=def

        ---------------------------------------------------------

        Staff Engineer
        Globex
        Berkeley
        $150K-$190K / year
        View job: https://www.linkedin.com/comm/jobs/view/4000000002/?trackingId=ghi

        ---------------------------------------------------------

        See more jobs: https://www.linkedin.com/comm/jobs/search-results/?keywords=x

        ----------------------------------------

        This email was intended for Steven Stout
        """;

    [Fact]
    public void An_alert_reads_as_title_company_location_and_a_clean_link()
    {
        var (leads, unread) = LinkedInAlert.Read(Alert.Replace("\r\n", "\n", StringComparison.Ordinal), "m9");
        Assert.Equal(0, unread);
        Assert.Equal(2, leads.Count);
        Assert.Equal(new Lead("Acme", "Principal Engineer (.NET)", "United States (Remote)", "https://www.linkedin.com/jobs/view/4000000001/", "m9"), leads[0]);
        Assert.Equal(("Globex", "Berkeley"), (leads[1].Company, leads[1].Location));
    }

    [Fact]
    public void A_lead_passes_only_remote_not_applied_and_under_the_cap()
    {
        var today = new DateOnly(2026, 10, 22);
        string[] App(int n, string company, string notes) => [n.ToString(System.Globalization.CultureInfo.InvariantCulture), "NA", company, "Greenhouse", "10/20/2026", "", notes];
        var master = new List<string[]>
        {
            App(1, "Initech", "SUBMITTED by Claude: Staff Engineer (Greenhouse 1)"),
            App(2, "Umbrella", "SUBMITTED: Lead"), App(3, "Umbrella", "SUBMITTED: Manager"), App(4, "Umbrella", "SUBMITTED: Architect"),
        };
        var leads = new[]
        {
            new Lead("Acme", "Principal Engineer", "Remote", "https://l/1/", "m"),
            new Lead("Acme", "Principal Engineer", "Remote", "https://l/1/", "m2"),
            new Lead("Globex", "Staff Engineer", "Berkeley", "https://l/2/", "m"),
            new Lead("Initech", "Staff Engineer", "Remote, US", "https://l/3/", "m"),
            new Lead("Umbrella Corp", "Principal", "Remote", "https://l/4/", "m"),
        };
        var judged = LeadsFilter.Judge(leads, master, [], today);
        Assert.Equal(4, judged.Count);
        Assert.Null(judged[0].Failed);
        Assert.StartsWith("not remote", judged[1].Failed, StringComparison.Ordinal);
        Assert.Equal("applied already, Count 1", judged[2].Failed);
        Assert.StartsWith("RULE 50: Umbrella Corp: 3 applications", judged[3].Failed, StringComparison.Ordinal);
    }
}
