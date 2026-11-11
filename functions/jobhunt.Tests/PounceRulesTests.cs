using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The pounce's rules (pounce1110 step 2b), ported from the reference cases of pounce_rules.py: the seat lists from his
/// targeting file, the two remote fixes JobsGates gained for both paths on two real postings, the leader seat as the
/// tracker writes it, the named address, the live instant, a day's line and the year-end budget flag. The people in
/// the leader and address cases are stand-ins with the shape of the real rows; this repository is public.
/// </summary>
public sealed class PounceRulesTests
{
    /// <summary>job-boards.greenhouse.io/ninjaone/jobs/4469473009, read 19:12 CST 11/10, location "United States".</summary>
    public const string NinjaOne = "We are flexible on remote working from home, if you are located in the USA and reside in one of the following states: "
        + "CA, CO, CT, FL, GA, IL, KS, MA, MD, ME, NJ, NC, NY, OH, OR, TN, TX, VA, and WA. We have physical offices in Austin, TX and Tampa, FL, if you prefer a hybrid option.";

    /// <summary>job-boards.greenhouse.io/singlestore/jobs/8318150, read 19:08 CST 11/10, location "United States".</summary>
    public const string SingleStore = "SingleStore is headquartered in San Francisco with offices in Sunnyvale, Raleigh, Seattle, Boston, London, Lisbon, Bangalore, Dublin and Kyiv. "
        + "We're proud to offer roles in a variety of locations across the United States. The base salary if based in Boston, Chicago, New York, Austin, "
        + "Phoenix, Las Vegas, Seattle, Washington, or Dallas is: $140,000 - $215,000 USD per year.";

    private static readonly DateOnly Today = new(2026, 11, 10);

    private static Candidate Role(string company, string title, string text, string stance = "Unknown", string location = "United States", int? band = 215_000) =>
        new("feed:greenhouse", company, title, "https://job-boards.greenhouse.io/x/jobs/1", "greenhouse", Today, location, stance, band, "feed", text);

    [Theory]
    [InlineData("Senior Software Engineer - Lead Platform")]
    [InlineData("Senior Software Engineer, Java | SaaS Backup")]
    [InlineData("Senior Technical Architect")]
    [InlineData("Senior Engineering Manager (Patient New Verticals)")]
    [InlineData("Technical Lead Manager")]
    [InlineData("Staff / Principal Software Engineer – Cloud Platform & SaaS")]
    [InlineData("Principal Software Engineer")]
    [InlineData("Principal Engineer, Platform Engineering")]
    [InlineData("Staff Product Engineer (Full Stack)")]
    [InlineData("Engineering Manager")]
    [InlineData("Sr. Manager, Software Engineering")]
    [InlineData("Software Architect")]
    [InlineData("Solutions Architect")]
    [InlineData("Manager, Application Development")]
    [InlineData(".NET AI Developer")]
    public void The_seats_the_11_10_lane_kept_are_seats(string title) => Assert.True(PounceRules.IsSeat(title), title);

    [Theory]
    [InlineData("Sr Pre-Sales Solution Architect")]
    [InlineData("Director of Technical Enablement")]
    [InlineData("Senior / Staff Product Manager, Patient Home Experience")]
    [InlineData("Lead Bioinformatics & Machine Learning Scientist")]
    [InlineData("Staff Product Designer, Family Experience")]
    [InlineData("Director, Sales Engineering")]
    [InlineData("Staff Data Scientist, SEO & AEO")]
    [InlineData("Manager, Marketing Operations & Revenue Technology")]
    [InlineData("Principal Product Manager (MajorClarity)")]
    [InlineData("Principal IAM Engineer")]
    [InlineData("Manager of Bioinformatics (Oncology Product Development)")]
    [InlineData("DevSecOps Architect – Software Supply Chain Security")]
    [InlineData("Principal, Business Development - Payer")]
    [InlineData("Sr. Technical Project Manager, Axon 911")]
    [InlineData("Staff Product Marketing Manager (PMM), Patient Growth")]
    [InlineData("Senior Technical Engagement Manager")]
    [InlineData("Staff Customer Reliability Engineer")]
    [InlineData("Technical Account Manager")]
    [InlineData("Staff Data Analyst")]
    [InlineData("Staff Security Engineer, Security Operations")]
    [InlineData("Principal Creative")]
    [InlineData("Forward Deployed Engineer")]
    [InlineData("Solutions Engineer")]
    [InlineData("Implementation Engineer")]
    public void The_twenty_four_the_seat_rule_drops_are_not_seats(string title) => Assert.False(PounceRules.IsSeat(title), title);

    [Fact]
    public void NinjaOne_names_states_without_Missouri_and_fails_both_gates_on_them()
    {
        Assert.Equal("CA, CO, CT, FL, GA, IL, KS, MA, MD, ME, NJ, NC, NY, OH, OR, TN, TX, VA, WA", JobsGates.StatesWithoutMissouri(NinjaOne));
        var verdict = JobsGates.Judge(Role("NinjaOne", "Principal Software Engineer", NinjaOne + " C# and .NET"), [], [], Today);
        Assert.Equal(JobsGates.NamedStates + "CA, CO, CT, FL, GA, IL, KS, MA, MD, ME, NJ, NC, NY, OH, OR, TN, TX, VA, WA", verdict.Failed);
        Assert.Equal(verdict.Failed, PounceRules.Gate(verdict, [], new HashSet<string>()));
    }

    [Fact]
    public void A_list_with_Missouri_passes_and_a_short_or_uncued_list_is_not_a_rule()
    {
        Assert.Null(JobsGates.StatesWithoutMissouri("You must reside in one of the following states: AZ, CO, FL, GA, MO, NC, TX, and VA."));
        Assert.Null(JobsGates.StatesWithoutMissouri("Offices in Austin, TX and Tampa, FL."));
        Assert.Null(JobsGates.StatesWithoutMissouri("Our customers include NY, CA, TX, FL, WA and OR agencies."));
        Assert.Null(JobsGates.StatesWithoutMissouri(""));
    }

    [Fact]
    public void SingleStore_never_says_remote_so_the_night_flags_it_and_the_pounce_refuses_it()
    {
        // The excerpt carries the location words; the stack sentence stands in for the rest of the posting, which the stack gate passed on 11/10.
        var verdict = JobsGates.Judge(Role("SingleStore", "Staff / Principal Software Engineer – Cloud Platform & SaaS", SingleStore + " The platform runs on Azure."), [], [], Today);
        Assert.Equal(JobsGates.CountryOnly, verdict.Remote);
        Assert.Null(verdict.Failed);
        Assert.Equal("country only, remote unsaid", PounceRules.Gate(verdict, [], new HashSet<string>()));
    }

    [Theory]
    [InlineData("United States", "Unknown", "Staff Software Engineer", "This is a fully remote role.", "remote")]
    [InlineData("United States", "Remote", "Staff Software Engineer", SingleStore, "remote")]
    [InlineData("United States", "Unknown", "Staff Software Engineer", "", "remote")]
    [InlineData("USA", "Unknown", "Staff Software Engineer (Remote)", SingleStore, "remote")]
    [InlineData("Anywhere in the US", "Unknown", "Staff Software Engineer", SingleStore, "remote")]
    [InlineData("US", "Unknown", "Staff Software Engineer", "We work from home across the country.", "remote")]
    public void A_country_with_the_word_remote_a_remote_stance_or_no_text_yet_reads_remote(string location, string stance, string title, string text, string read) =>
        Assert.Equal(read, JobsGates.RemoteRead(Role("Acme", title, text, stance, location)));

    [Fact]
    public void A_leader_seat_is_read_as_the_tracker_writes_it()
    {
        // The shapes of four tracker rows of 11/5: the seat inside the company cell with its parenthesis open, the seat
        // in a closed parenthesis, the seat in Notes, and a recruiter.
        Assert.True(PounceRules.IsLeader("Pat Rowe", "Phreesia (VP Engineering", ""));
        Assert.True(PounceRules.IsLeader("Lee Park", "Backblaze (CTO)", ""));
        Assert.True(PounceRules.IsLeader("Sam Ortiz", "HHAeXchange", "SCOPED CONNECTION REQUEST SENT. Director, Software Engineering, 2nd."));
        Assert.False(PounceRules.IsLeader("Ana Bell", "LINQ", "CONNECTION REQUEST SENT. Senior Talent Acquisition Coordinator at LINQ."));
        Assert.False(PounceRules.IsLeader("Ana Bell", "LINQ", new string('x', 300) + " Director"));
    }

    [Theory]
    [InlineData("info@filevine.com", "J.R. Rowe", false)]
    [InlineData("careers@bloomerang.com", "Tommy Vale", false)]
    [InlineData("digicertpress@digicert.com", "Jason Sable", false)]
    [InlineData("media.help@esri.com", "Jay Thorne", false)]
    [InlineData("no address", "Jay Thorne", false)]
    [InlineData("jsable@digicert.com", "Jason Sable", true)]
    [InlineData("tommy.vale@bloomerang.com", "Tommy Vale", true)]
    [InlineData("jay@esri.com", "Jay Thorne", true)]
    public void An_address_is_named_when_it_carries_the_person_s_own_name(string address, string name, bool named) =>
        Assert.Equal(named, PounceRules.IsNamedAddress(address, name));

    [Fact]
    public void The_live_instant_is_the_board_s_unless_it_is_after_the_first_sighting()
    {
        var seen = new DateTimeOffset(2026, 11, 11, 15, 15, 0, TimeSpan.Zero);
        Assert.Equal((seen.AddMinutes(-11), true), PounceClock.LiveAt(seen.AddMinutes(-11), seen));
        Assert.Equal((seen, false), PounceClock.LiveAt(null, seen));
        Assert.Equal((seen, false), PounceClock.LiveAt(seen.AddMinutes(45), seen));
        Assert.Equal(40, PounceClock.WholeMinutes(seen, seen.AddMinutes(40).AddSeconds(59)));
    }

    [Fact]
    public void A_day_s_line_is_its_cohort_s_median_worst_and_waiting_on_the_Central_day()
    {
        static DateTimeOffset Utc(int h, int m, int d = 11) => new(2026, 11, d, h, m, 0, TimeSpan.Zero);
        (DateTimeOffset, DateTimeOffset?)[] roles = [(Utc(15, 0), Utc(15, 40)), (Utc(16, 0), Utc(18, 5)), (Utc(20, 0), null), (Utc(3, 0), Utc(3, 30))];
        Assert.Equal(new PounceDay(new DateOnly(2026, 11, 11), 3, 2, 83, 125, 1, 90), PounceClock.DayLine(new DateOnly(2026, 11, 11), roles, Utc(21, 30)));
        Assert.Equal(new PounceDay(new DateOnly(2026, 11, 10), 1, 1, 30, 30, 0, 0), PounceClock.DayLine(new DateOnly(2026, 11, 10), roles, Utc(21, 30)));
        Assert.Equal(new PounceDay(new DateOnly(2026, 11, 9), 0, 0, 0, 0, 0, 0), PounceClock.DayLine(new DateOnly(2026, 11, 9), roles, Utc(21, 30)));
        Assert.Equal(["caught", "touched", "median_min", "worst_min", "waiting", "oldest_wait_min"], new PounceDay(Today, 0, 0, 0, 0, 0, 0).Named.Select(n => n.Name));
        Assert.Equal((0, 7, 4, 3), (PounceClock.Median([]), PounceClock.Median([7]), PounceClock.Median([3, 4, 9]), PounceClock.Median([2, 3])));
    }

    [Fact]
    public void The_budget_flag_holds_for_21_days_and_not_past_the_year_s_end()
    {
        var live = new DateTimeOffset(2026, 11, 11, 15, 0, 0, TimeSpan.Zero);
        Assert.True(PounceClock.BudgetFlag(live, new DateOnly(2026, 11, 11)));
        Assert.True(PounceClock.BudgetFlag(live, new DateOnly(2026, 12, 2)));
        Assert.False(PounceClock.BudgetFlag(live, new DateOnly(2026, 12, 3)));
        Assert.False(PounceClock.BudgetFlag(new DateTimeOffset(2026, 12, 28, 15, 0, 0, TimeSpan.Zero), new DateOnly(2027, 1, 2)));
    }

    [Fact]
    public void The_blacklist_blocks_a_company_only_on_a_company_wide_row()
    {
        // The shape of BLACKLIST.md's rows (read 23:4x CST 11/10): three people, two companies whole, one job only.
        const string file = "| Added | Person | Address | Company | Steve's words | Why |\n|---|---|---|---|---|---|\n"
            + "| 2026-09-29 | A Person | a@agency.example | Agency Consulting LLC, Somewhere NJ | x | y |\n"
            + "| 2026-11-09 | ALL OF PETDESK (company-wide; includes Petvisor and Vetstoria; recruiter A Person) | any @petdesk.com | PetDesk (Remote Principal Engineer) | x | y |\n"
            + "| 2026-11-10 | THE KEYPATH EDUCATION LEAD SOFTWARE DEVELOPER JOB ONLY (job-level, not the company) | LinkedIn job 1 | Keypath Education | x | y |\n"
            + "| 2026-11-10 | ALL OF FOUNDATION FINANCE COMPANY, LLC (company-wide, CONDITIONAL) | icims | Foundation Finance Company, LLC, Rothschild WI (Software Architect) | x | y |\n";
        Assert.Equal(["PetDesk", "Petvisor", "Vetstoria", "Foundation Finance Company"], PounceRules.Blacklisted(file));
        var verdict = JobsGates.Judge(Role("Foundation Finance", "Software Architect", "C# on Azure. Fully remote."), [], [], Today);
        Assert.Equal("on the blacklist", PounceRules.Gate(verdict, PounceRules.Blacklisted(file), new HashSet<string>()));
        Assert.Null(PounceRules.Gate(JobsGates.Judge(Role("Keypath Education", "Software Architect", "C# on Azure. Fully remote."), [], [], Today), PounceRules.Blacklisted(file), new HashSet<string>()));
        Assert.Empty(PounceRules.Blacklisted(""));
    }

    [Fact]
    public void A_seat_outside_his_list_and_a_role_passed_already_are_refused_in_order()
    {
        var product = JobsGates.Judge(Role("Acme", "Principal Product Manager, AI Platform", "AI agents. Fully remote."), [], [], Today);
        Assert.Null(product.Failed);
        Assert.Equal("not a seat his targeting file names", PounceRules.Gate(product, [], new HashSet<string>()));
        var role = JobsGates.Judge(Role("Acme", "Principal Software Engineer", "C# on Azure. Fully remote."), [], [], Today);
        Assert.Null(PounceRules.Gate(role, [], new HashSet<string>()));
        Assert.Equal("the same role passed in the last 30 days", PounceRules.Gate(role, [], new HashSet<string> { JobsGates.Same(role.Candidate) }));
    }
}
