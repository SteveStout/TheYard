using System.Text.RegularExpressions;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// Track A on (ADR-118, the addendum "Track A sends first; the finder behind the port"): Template A v2 is the one A the
/// send can select, sixty words or fewer with its one link and no dash or tell; the week's applications with a person
/// the jobs lane named become one Track A lead a company however many passes run; a shared mailbox never reaches the
/// send port; Track A goes first, the oldest application first; and shadow writes its rows and sends nothing.
/// </summary>
public sealed partial class TrackATests
{
    // 8:00 CST on Friday 6 November 2026.
    private static readonly DateTimeOffset Eight = new(2026, 11, 6, 14, 0, 5, TimeSpan.Zero);

    [Fact]
    public void A_v2_is_the_only_A_the_send_selects_and_v1_stays_in_the_code_for_history()
    {
        Assert.Equal(("A", "v2"), (CampaignTemplate.For('A').Id, CampaignTemplate.For('A').Version));
        Assert.Equal("v1", CampaignTemplate.For('B').Version);
        Assert.Contains(CampaignTemplate.All, t => t.Id == "A" && t.Version == "v1");
        Assert.Equal(["v2"], CampaignTemplate.All.Where(t => t.Id == "A" && t.Version == CampaignTemplate.CurrentFor('A')).Select(t => t.Version));
    }

    [Fact]
    public void A_v2_is_sixty_words_or_fewer_with_one_link_and_no_dash_or_tell()
    {
        var email = TemplateRender.Render(CampaignTemplate.For('A'), TemplateRender.Values(CampaignSendTests.Lead('A'), Eight), TemplateRender.Optional);
        Assert.True(email.Complete, string.Join(", ", email.Blanks));
        var words = email.Text!.Split('\n').Where(line => !line.StartsWith("https://", StringComparison.Ordinal)).SelectMany(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
        Assert.InRange(words.Count, 1, 60);
        Assert.Single(Regex.Matches(email.Text, "https://"));
        Assert.DoesNotContain('\u2014', email.Subject + email.Text + email.Html);
        Assert.DoesNotContain('\u2013', email.Subject + email.Text + email.Html);
        Assert.Empty(Tells.Find(email.Subject + "\n" + email.Text));
    }

    [Fact]
    public async Task Two_passes_write_one_Track_A_lead_a_company_and_a_shared_mailbox_is_never_its_address()
    {
        using var scratch = new Scratch();
        scratch.Master("3501,NA,Northwind,Greenhouse,11/6/2026,,SUBMITTED by the 6 AM lane: Principal Engineer\r\n3502,NA,Contoso,Lever,11/5/2026,,SUBMITTED: Staff Engineer\r\n3503,NA,Fabrikam,Lever,10/20/2026,,SUBMITTED: Lead Engineer\r\n3504,NA,Tailspin,Lever,11/5/2026,,SUBMITTED: Lead Engineer\r\n");
        scratch.Write(TrackAContacts.File(new DateOnly(2026, 11, 6)), string.Join('\t', TrackAContacts.Columns) + "\n"
            + Contact("Northwind", "northwind.com", "Dana", "Dana Ruiz", "dana@northwind.com") + "\n"
            + Contact("Contoso", "contoso.com", "Lee", "Lee Park", "info@contoso.com") + "\n"
            + Contact("Fabrikam", "fabrikam.com", "Sam", "Sam Roe", "") + "\n"
            + "broken\trow\n");
        var book = new Book();
        var feed = new TrackAFeed(scratch.Store, book, new Mx(), new Fixed(Eight));

        string first = await feed.RunAsync(CancellationToken.None);
        string second = await feed.RunAsync(CancellationToken.None);

        Assert.Equal("track A: 3 applications in 7 days, 3 named contacts (1 unread), 2 rows written (1 waiting on a named address), 0 already had one, 1 with no named person", first);
        Assert.Equal("track A: 3 applications in 7 days, 3 named contacts (1 unread), 0 rows written (0 waiting on a named address), 2 already had one, 1 with no named person", second);
        Assert.Equal(["contoso.com", "northwind.com"], book.Leads.Select(l => l.Lead.CompanyKey).Order());
        var northwind = book.Leads.Single(l => l.Lead.CompanyKey == "northwind.com").Lead;
        Assert.Equal(("A", "dana@northwind.com", 3501, "abc123"), (northwind.Track, northwind.ContactEmail, northwind.AppliedCount!.Value, northwind.RefCode));
        Assert.Null(book.Leads.Single(l => l.Lead.CompanyKey == "contoso.com").Lead.ContactEmail);
        Assert.Equal(2, scratch.Text(TrackAFeed.Log).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void A_Track_A_lead_at_a_shared_mailbox_or_with_no_address_is_held_and_never_sent()
    {
        var info = CampaignSendTests.Lead('A', edit: b => b.ContactEmail = "info@acmedental.com");
        var none = CampaignSendTests.Lead('A', edit: b => b.ContactEmail = null);
        var named = CampaignSendTests.Lead('A');
        Assert.Equal(TrackAFeed.NoNamedAddress, Check(info).Verdict.Reason);
        Assert.Equal(TrackAFeed.NoNamedAddress, Check(none).Verdict.Reason);
        Assert.True(Check(named).Verdict.Allowed);
        Assert.True(TrackAContacts.IsGeneralMailbox("Careers@acme.com") && !TrackAContacts.IsGeneralMailbox("dana@acme.com") && !TrackAContacts.IsGeneralMailbox(null));
    }

    [Fact]
    public void Of_twelve_eligible_leads_the_four_Track_A_ones_go_first_oldest_application_first()
    {
        var b = Enumerable.Range(1, 8).Select(i => new RankedLead(i, i % 3, 20 - i, Eight.AddDays(-i)));
        var a = new[] { 3, 1, 4, 2 }.Select(days => new RankedLead(100 + days, 2, 1, Eight) { Track = 'A', AppliedAt = Eight.AddDays(-days) });
        var order = SendOrder.Sort(b.Concat(a)).ToList();
        Assert.Equal(12, order.Count);
        Assert.Equal([104L, 103L, 102L, 101L], order.Take(4).Select(r => r.LeadId));
        Assert.All(order.Skip(4), r => Assert.Equal('B', r.Track));
    }

    [Fact]
    public async Task Shadow_writes_the_Track_A_rows_first_and_sends_nothing()
    {
        var leads = Enumerable.Range(1, 8).Select(i => CampaignSendTests.Lead(company: $"Bco{i}", key: $"bco{i}.com"))
            .Concat(Enumerable.Range(1, 4).Select(i => CampaignSendTests.Lead('A', company: $"Aco{i}", key: $"aco{i}.com"))).ToList();
        using var scratch = new Scratch();
        scratch.Master("");
        var db = new CampaignSendTests.MemoryCampaign(leads);
        var mail = new CampaignSendTests.CountingSender();
        var result = await new CampaignSend(db, db, mail, scratch.Store, new Fixed(new DateTimeOffset(2026, 10, 24, 13, 0, 5, TimeSpan.Zero))) { Spacing = TimeSpan.Zero }.RunAsync(CampaignMode.Shadow, CancellationToken.None);

        Assert.Equal(12, result.Eligible);
        Assert.Equal(10, result.Written);
        Assert.Equal(["aco1.com", "aco2.com", "aco3.com", "aco4.com"], db.Emails.Take(4).Select(e => e.Key).Order());
        Assert.All(db.Emails.Take(4), e => Assert.Equal("v2", e.TemplateVersion));
        Assert.All(db.Emails, e => Assert.True(e.Shadow));
        Assert.Equal(0, mail.Calls);
    }

    [Fact]
    public void The_window_s_statement_names_only_columns_the_sql_project_declares()
    {
        string Table(string name) => File.ReadAllText(Path.Combine(Root(), "functions", "JobHunt.Database", "Tables", name + ".sql"));
        var leads = Regex.Matches(Table("Leads"), @"^\s*\[(\w+)\]", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var companies = Regex.Matches(Table("Companies"), @"^\s*\[(\w+)\]", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        string sql = (string)typeof(SqlTrackABook).GetField("TrackAKeys", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetRawConstantValue()!;
        Assert.All(Regex.Matches(sql, @"\bl\.\[(\w+)\]").Select(m => m.Groups[1].Value), column => Assert.Contains(column, leads));
        Assert.All(Regex.Matches(sql, @"\bc\.\[(\w+)\]").Select(m => m.Groups[1].Value), column => Assert.Contains(column, companies));
        Assert.DoesNotMatch(@"\b(INSERT|UPDATE|DELETE|MERGE)\b", sql);
    }

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "functions", "JobHunt.Database")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("functions/JobHunt.Database not found above " + AppContext.BaseDirectory);
    }

    private static (CampaignVerdict Verdict, RenderedEmail? Email, bool Block) Check(LeadRecord lead) =>
        CampaignSend.Check(lead, [], [], [], new DateOnly(2026, 10, 24), new DateTimeOffset(2026, 10, 24, 13, 0, 5, TimeSpan.Zero));

    private static string Contact(string company, string key, string first, string full, string email) => string.Join('\t',
        company, key, "Principal Engineer", $"https://jobs.lever.co/{key}/1", first, full, "VP of Engineering", $"https://{key}/team", email, email.Length == 0 ? "" : $"https://{key}/team",
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
        public List<(int CompanyId, SpoolLead Lead)> Leads { get; } = [];

        public Task<IReadOnlyList<string>> TrackAKeysAsync(DateTimeOffset since, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([.. Leads.Where(l => l.Lead.AppliedAt >= since).Select(l => l.Lead.CompanyKey).Distinct()]);

        public Task<int> CompanyIdAsync(SpoolCompany company, string run, DateTimeOffset at, CancellationToken ct) => Task.FromResult(company.Key.Length);

        public Task SaveLeadAsync(int companyId, SpoolLead lead, bool? mx, int strength, string inputsHash, string run, DateTimeOffset at, CancellationToken ct)
        {
            Leads.Add((companyId, lead));
            return Task.CompletedTask;
        }
    }
}
