using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// BLACKLIST.md read as rules (fitgate1110-f3 Part A): a company row blocks the company and the names it says it
/// includes, a job row blocks that one posting and leaves the company open, and a person row blocks the person and not
/// the person's company. The night's picker and the leads filter fail a hit as "blacklisted: &lt;row date&gt;" ahead of every
/// other gate. The rows are BLACKLIST.md's own, read 11/12, cut to their shapes: the people the rows name besides the
/// one person row are left out, and so are the address and phone the person row carries.
/// </summary>
public sealed class BlacklistTests
{
    private static readonly DateOnly Today = new(2026, 11, 12);

    private const string File = """
        > RULES FIRST: read systemRules.md before acting on this file.

        # BLACKLIST. SENDERS STEVE HAS CUT OFF. CREATED 2026-09-29 BY REPLYLANE ON HIS WORD.

        | Added | Person | Address | Company | Steve's words | Why |
        |---|---|---|---|---|---|
        | 2026-09-29 15:11 CDT | Vikas Yadav | an apetan.com address | Apetan Consulting LLC, Rutherford NJ | "Put him on the blacklist he is making no effort to pull me in or look at my value" | Mass-mail contract pitch. Red flag 6/11. |
        | 2026-11-09 13:13 CST | ALL OF PETDESK (company-wide; includes Petvisor and Vetstoria; the recruiter, the Director and every leader contacted by petdeskconnect1109) | any @petdesk.com or @petvisor.com address; LinkedIn threads with PetDesk staff; Lever no-reply@hire.lever.co mail about PetDesk | PetDesk (Remote Principal Engineer, New Products, Lever e308da7c, applied 11/5 Count 3387) | "We've put a blacklist on pet desk because they don't have us tech staff" | Company-level block: Steve reads PetDesk as having no US tech staff. |
        | 2026-11-10 08:07 CST | THE KEYPATH EDUCATION LEAD SOFTWARE DEVELOPER JOB ONLY (job-level, not the company; its manager named) | LinkedIn job 4463107746 (Schaumburg IL, Remote) | Keypath Education | "black list thsi job" | Posted range $125,000-$146,000, under his $170K floor. |

        Watch, not blacklisted: other Apetan Consulting senders.
        """;

    private const string KeypathJob = "https://www.linkedin.com/jobs/view/4463107746/";

    private static Candidate Role(string company, string title, string url) =>
        new("feed:lever", company, title, url, "lever", Today, "United States", "Remote", 210_000, "feed", "C# on Azure. Fully remote.");

    [Fact]
    public void A_company_row_fails_the_company_and_its_aliases_ahead_of_every_other_gate()
    {
        var blacklist = Blacklist.Read(File);
        Assert.Equal(["PetDesk", "Petvisor", "Vetstoria"], blacklist.Companies);
        var petdesk = JobsGates.Judge(Role("PetDesk", "Principal Engineer, New Products", "https://jobs.lever.co/petdesk/e308da7c"), [], [], Today, blacklist);
        Assert.Equal("blacklisted: 2026-11-09", petdesk.Failed);
        // A Petvisor posting 90 days old in Toronto fails three other gates; the blacklist is the one written.
        var stale = JobsGates.Judge(Role("Petvisor", "Principal Engineer", "https://jobs.lever.co/petvisor/1") with { Posted = Today.AddDays(-90), Location = "Toronto, ON", RemoteStance = "Onsite" }, [], [], Today, blacklist);
        Assert.Equal("blacklisted: 2026-11-09", stale.Failed);
        var lead = Assert.Single(LeadsFilter.Judge([new Lead("PetDesk (PetVisor)", "Principal Engineer", "Toronto, ON", "https://www.linkedin.com/jobs/view/4400000001/", "m1")], [], [], Today, blacklist));
        Assert.Equal("blacklisted: 2026-11-09", lead.Failed);
    }

    [Fact]
    public void A_job_row_blocks_that_job_and_the_company_stays_open()
    {
        var blacklist = Blacklist.Read(File);
        Assert.Equal("blacklisted: 2026-11-10", JobsGates.Judge(Role("Keypath Education", "Lead Software Developer", KeypathJob), [], [], Today, blacklist).Failed);
        Assert.Null(JobsGates.Judge(Role("Keypath Education", "Principal Software Engineer", "https://www.linkedin.com/jobs/view/4471234567/"), [], [], Today, blacklist).Failed);
        var leads = LeadsFilter.Judge(
            [new Lead("Keypath Education", "Lead Software Developer", "United States (Remote)", KeypathJob, "m1"),
             new Lead("Keypath Education", "Principal Software Engineer", "United States (Remote)", "https://www.linkedin.com/jobs/view/4471234567/", "m2")],
            [], [], Today, blacklist);
        Assert.Equal(["blacklisted: 2026-11-10", null], leads.Select(l => l.Failed));
    }

    [Fact]
    public void A_person_row_blocks_the_person_and_not_the_company()
    {
        var blacklist = Blacklist.Read(File);
        Assert.Equal("blacklisted: 2026-09-29", blacklist.Hit("Apetan Consulting LLC", "Vikas Yadav")?.Reason);
        Assert.Equal("blacklisted: 2026-09-29", blacklist.Hit("", "vikas yadav (Apetan)")?.Reason);
        Assert.Null(blacklist.Hit("Apetan Consulting LLC", "Another Recruiter"));
        Assert.Null(JobsGates.Judge(Role("Apetan Consulting LLC", "Principal Software Engineer", "https://example.com/1"), [], [], Today, blacklist).Failed);
        Assert.Null(Blacklist.Read("").Hit("PetDesk", "Vikas Yadav", KeypathJob));
    }

    [Fact]
    public void The_pounce_keeps_its_company_list_through_the_reader()
    {
        Assert.Equal(Blacklist.Read(File).Companies, PounceRules.Blacklisted(File));
    }
}
