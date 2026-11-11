using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// Template A v3 (ADR-118, the addendum "A leader's email asks to be sent down"): the leaders lane names the leader one
/// level above the seat (RULE 73), so the Track A email it leads to closes with the ask to be sent down to the team, in
/// the words Steve approved on 11 November; the lane's row says so in a Lane cell the jobs lane's rows do not carry; the
/// lead keeps it as the run that wrote it; and only that lead gets v3. Every other A keeps v2, B keeps v1, and v2 stays
/// readable. Every person, company and address here is made up.
/// </summary>
public sealed class LeaderTemplateTests
{
    // 8:00 CST on Friday 6 November 2026.
    private static readonly DateTimeOffset Eight = new(2026, 11, 6, 14, 0, 5, TimeSpan.Zero);

    /// <summary>The text of J:\mentor\AUTOHUNT-L-DONE-2026-11-09.md, LEFT FOR STEVE A, approved "Send approve" 16:47 CST 11/11.</summary>
    private const string Approved = """
        Subject: Remote {RoleTitle} at {Company}

        {FirstName},

        Good morning! I just applied for the Remote {RoleTitle} opening on your team.

        I build .NET on Azure with AI in the loop, and TheYard shows how:

        {SiteLink}

        Who on your team should I talk to about the {RoleTitle} role?

        Steve

        """;

    [Fact]
    public void A_v3_is_the_approved_text_word_for_word_and_v2_stays_beside_it()
    {
        var v3 = CampaignTemplate.All.Single(t => t.Id == "A" && t.Version == "v3");
        Assert.Equal(Approved.Replace("\r\n", "\n", StringComparison.Ordinal), v3.Text.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Contains(CampaignTemplate.All, t => t.Id == "A" && t.Version == "v2" && t.Text.Contains("Would it be a bad idea to set up a time to talk?", StringComparison.Ordinal));
        Assert.Equal("v2", CampaignTemplate.CurrentFor('A'));
    }

    [Fact]
    public void Only_a_Track_A_lead_the_leaders_lane_wrote_gets_A_v3()
    {
        var leader = CampaignSendTests.Lead('A', edit: b => b.CreatedBy = TrackAFeed.RunFor("leaders-20261111"));
        var jobs = CampaignSendTests.Lead('A', edit: b => b.CreatedBy = TrackAFeed.RunFor(null));
        var unknown = CampaignSendTests.Lead('A', edit: b => b.CreatedBy = null);
        var cold = CampaignSendTests.Lead('B', edit: b => b.CreatedBy = TrackAFeed.RunFor("leaders-20261111"));

        Assert.Equal(("A", "v3"), (CampaignTemplate.For(leader).Id, CampaignTemplate.For(leader).Version));
        Assert.Equal(("A", "v2"), (CampaignTemplate.For(jobs).Id, CampaignTemplate.For(jobs).Version));
        Assert.Equal(("A", "v2"), (CampaignTemplate.For(unknown).Id, CampaignTemplate.For(unknown).Version));
        Assert.Equal(("B", "v1"), (CampaignTemplate.For(cold).Id, CampaignTemplate.For(cold).Version));
    }

    [Theory]
    [InlineData("leaders-20261111", "track-a/leaders-20261111")]
    [InlineData("leaders", "track-a/leaders")]
    [InlineData(null, "track-a")]
    [InlineData("", "track-a")]
    [InlineData("jobs-20261111", "track-a")]
    [InlineData("leaders-x; DROP", "track-a")]
    [InlineData("leadersx", "track-a")]
    public void The_run_names_the_leaders_lane_only_when_the_cell_is_the_lane_s_own_name(string? lane, string run)
    {
        Assert.Equal(run, TrackAFeed.RunFor(lane));
        Assert.Equal(run != "track-a", TrackAFeed.IsLeadersRun(run));
    }

    [Fact]
    public void A_v3_renders_byte_for_byte_sixty_words_or_fewer_with_one_link_and_no_dash_or_tell()
    {
        var lead = CampaignSendTests.Lead('A', edit: b => b.CreatedBy = TrackAFeed.RunFor("leaders-20261111"));
        var email = TemplateRender.Render(CampaignTemplate.For(lead), TemplateRender.Values(lead, Eight), TemplateRender.Optional);

        Assert.True(email.Complete, string.Join(", ", email.Blanks));
        Assert.Equal(Fixture("A-v3.subject.txt").TrimEnd('\n'), email.Subject);
        Assert.Equal(Fixture("A-v3.txt"), email.Text);
        Assert.Equal(Fixture("A-v3.html"), email.Html);
        var words = email.Text!.Split('\n').Where(line => !line.StartsWith("https://", StringComparison.Ordinal)).SelectMany(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
        Assert.InRange(words.Count, 1, 60);
        Assert.Single(Regex.Matches(email.Text, "https://"));
        Assert.DoesNotContain('\u2014', email.Subject + email.Text + email.Html);
        Assert.DoesNotContain('\u2013', email.Subject + email.Text + email.Html);
        Assert.Empty(Tells.Find(email.Subject + "\n" + email.Text));
    }

    [Fact]
    public void The_Lane_cell_is_read_when_the_row_has_it_and_a_jobs_lane_row_of_seventeen_reads_as_before()
    {
        string seventeen = Contact("Northwind", "northwind.com", "Dana", "Dana Ruiz");
        string eighteen = Contact("Contoso", "contoso.com", "Lee", "Lee Park") + "\tleaders-20261111";
        var (rows, unread) = TrackAContacts.Parse(string.Join('\t', TrackAContacts.Columns) + "\n" + seventeen + "\n" + eighteen + "\n");

        Assert.Equal(0, unread);
        Assert.Null(rows.Single(r => r.Company == "Northwind").Lane);
        Assert.Equal("leaders-20261111", rows.Single(r => r.Company == "Contoso").Lane);
        Assert.Equal("Lane", TrackAContacts.LaneColumn);
        Assert.DoesNotContain(TrackAContacts.LaneColumn, TrackAContacts.Columns);
    }

    [Fact]
    public async Task The_feed_writes_a_leaders_lane_row_under_the_lane_s_run_and_a_jobs_lane_row_under_track_a()
    {
        using var scratch = new Scratch();
        scratch.Master("3601,NA,Northwind,Greenhouse,11/6/2026,,SUBMITTED by the 6 AM lane: Principal Engineer\r\n3602,NA,Contoso,Lever,11/5/2026,,SUBMITTED: Staff Engineer\r\n");
        scratch.Write(TrackAContacts.File(new DateOnly(2026, 11, 6)), string.Join('\t', TrackAContacts.Columns) + "\n"
            + Contact("Northwind", "northwind.com", "Dana", "Dana Ruiz") + "\n"
            + Contact("Contoso", "contoso.com", "Lee", "Lee Park") + "\tleaders-20261106\n");
        var book = new Book();

        await new TrackAFeed(scratch.Store, book, new Mx(), new Fixed(Eight)).RunAsync(CancellationToken.None);

        Assert.Equal("track-a", book.Runs["northwind.com"]);
        Assert.Equal("track-a/leaders-20261106", book.Runs["contoso.com"]);
    }

    [Fact]
    public async Task Shadow_logs_v3_for_the_leaders_lane_s_lead_and_v2_for_the_jobs_lane_s()
    {
        var leader = CampaignSendTests.Lead('A', company: "Aco1", key: "aco1.com", edit: b => b.CreatedBy = TrackAFeed.RunFor("leaders-20261024"));
        var jobs = CampaignSendTests.Lead('A', company: "Aco2", key: "aco2.com", edit: b => b.CreatedBy = TrackAFeed.RunFor(null));
        using var scratch = new Scratch();
        scratch.Master("");
        var db = new CampaignSendTests.MemoryCampaign([leader, jobs]);
        var mail = new CampaignSendTests.CountingSender();

        await new CampaignSend(db, db, mail, scratch.Store, new Fixed(new DateTimeOffset(2026, 10, 24, 13, 0, 5, TimeSpan.Zero))) { Spacing = TimeSpan.Zero }
            .RunAsync(CampaignMode.Shadow, CancellationToken.None);

        var v3 = db.Emails.Single(e => e.Key == "aco1.com");
        var v2 = db.Emails.Single(e => e.Key == "aco2.com");
        Assert.Equal(("A", "v3"), (v3.TemplateId, v3.TemplateVersion));
        Assert.Contains("Who on your team should I talk to about the Staff Software Engineer role?", v3.BodyText, StringComparison.Ordinal);
        Assert.Equal(("A", "v2"), (v2.TemplateId, v2.TemplateVersion));
        Assert.Contains("Would it be a bad idea to set up a time to talk?", v2.BodyText, StringComparison.Ordinal);
        Assert.Equal(0, mail.Calls);
    }

    [Fact]
    public void The_open_leads_statement_reads_the_run_that_wrote_the_lead()
    {
        string sql = (string)typeof(SqlCampaignStore).Assembly.GetType("JobHunt.Infrastructure.Features.Campaign.CampaignSql", throwOnError: true)!
            .GetField("OpenLeads", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        Assert.Contains("l.[CreatedBy]", sql, StringComparison.Ordinal);
    }

    private static string Fixture(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "functions", "jobhunt.Tests", "Fixtures", "campaign", name);
            if (File.Exists(path))
            {
                return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
            }
        }
        throw new InvalidOperationException("Fixtures/campaign/" + name + " not found");
    }

    private static string Contact(string company, string key, string first, string full) => string.Join('\t',
        company, key, "Principal Engineer", $"https://jobs.lever.co/{key}/1", first, full, "VP of Engineering", $"https://{key}/team", $"{first.ToLowerInvariant()}@{key}", $"https://{key}/team",
        "abc123", "https://theyard.stevenstout.biz/resume?src=email&r=abc123", "https://theyard.stevenstout.biz/?r=abc123", "https://theyard.stevenstout.biz/api/go/github?r=abc123",
        "https://theyard.stevenstout.biz/api/go/linkedin?r=abc123", "https://theyard.stevenstout.biz/about?r=abc123", "platform");

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Mx : IMxCheck
    {
        public Task<bool?> HasMxAsync(string domain, CancellationToken ct) => Task.FromResult<bool?>(true);
    }

    private sealed class Book : ITrackABook
    {
        public Dictionary<string, string> Runs { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<string>> TrackAKeysAsync(DateTimeOffset since, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([.. Runs.Keys]);

        public Task<int> CompanyIdAsync(SpoolCompany company, string run, DateTimeOffset at, CancellationToken ct) => Task.FromResult(company.Key.Length);

        public Task SaveLeadAsync(int companyId, SpoolLead lead, bool? mx, int strength, string inputsHash, string run, DateTimeOffset at, CancellationToken ct)
        {
            Runs[lead.CompanyKey] = run;
            return Task.CompletedTask;
        }
    }
}
