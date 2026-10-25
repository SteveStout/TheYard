using System.Text;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The renderer and the send path (ADR-118, Decision 3; the review, F4, F7, F8, F12): the approved templates
/// rendered byte for byte against checked-in files, every variable given by the row, a blank named, the
/// switch's words, the 8 o'clock hour, and the pass over memory stores: shadow writes and never reaches the
/// mail port, on sends the cap and the ramp and no more, a second firing in the day sends nothing, and the
/// rules hold leads with their reasons named. Every person, company and address here is made up.
/// </summary>
public sealed class CampaignSendTests
{
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

    // 8:00 CDT on Saturday 24 October 2026.
    private static readonly DateTimeOffset Eight = new(2026, 10, 24, 13, 0, 5, TimeSpan.Zero);

    private static int _next;

    internal static LeadRecord Lead(char track = 'B', string company = "Acme Dental", string key = "acmedental.com", Action<LeadRecordBuilder>? edit = null)
    {
        var b = new LeadRecordBuilder
        {
            Id = Interlocked.Increment(ref _next),
            Track = track,
            Company = new CompanyRecord(Interlocked.Increment(ref _next), key, null, [company], "Remote", null, null, 2, false),
            CompanyName = company,
            RoleTitle = track == 'A' ? "Staff Software Engineer" : null,
            WhatTheyDo = "builds practice management software for dental offices",
            FirstName = "Pat",
            ContactName = "Pat Lee",
            ContactTitle = "VP of Engineering",
            ContactEmail = $"pat@{key}",
            EmailSource = $"https://{key}/team",
            MxChecked = true,
            AppliedAt = track == 'A' ? Eight.AddHours(-1) : null,
            FirstSeen = new DateOnly(2026, 10, 20),
            RefCode = "abc123",
            ResumeLink = "https://theyard.stevenstout.biz/resume?src=email&r=abc123",
            SiteLink = "https://theyard.stevenstout.biz/?r=abc123",
            GitHubLink = "https://theyard.stevenstout.biz/api/go/github?r=abc123",
            LinkedInLink = "https://theyard.stevenstout.biz/api/go/linkedin?r=abc123",
            AboutLink = "https://theyard.stevenstout.biz/about?r=abc123",
            CreatedAt = Eight.AddDays(-2),
        };
        edit?.Invoke(b);
        return b.Build();
    }

    [Theory]
    [InlineData('A', "A-v1")]
    [InlineData('B', "B-v1")]
    public void F12_each_approved_template_renders_byte_for_byte(char track, string fixture)
    {
        var email = TemplateRender.Render(CampaignTemplate.For(track), TemplateRender.Values(Lead(track), Eight));
        Assert.True(email.Complete, string.Join(", ", email.Blanks));
        Assert.Equal(Fixture(fixture + ".subject.txt").TrimEnd('\n'), email.Subject);
        Assert.Equal(Fixture(fixture + ".txt"), email.Text);
        Assert.Equal(Fixture(fixture + ".html"), email.Html);
        Assert.DoesNotContain("**", email.Text, StringComparison.Ordinal);
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(email.Html!, "href=\"[^\"]+\\?[^\"]*r=abc123\"").Count);
    }

    [Fact]
    public void F12_an_optional_line_renders_with_its_values_and_goes_whole_without_them()
    {
        var template = new CampaignTemplate("B", "test", Fixture("B-optional.md"));
        var with = Lead(edit: b => b.Company = b.Company with { LastDotNetPostingTitle = "Senior .NET Engineer", LastDotNetPostingAt = new DateOnly(2026, 10, 9) });
        Assert.Equal(Fixture("B-optional-with.txt"), TemplateRender.Render(template, TemplateRender.Values(with, Eight)).Text);
        Assert.Equal(Fixture("B-optional-without.txt"), TemplateRender.Render(template, TemplateRender.Values(Lead(), Eight)).Text);
    }

    [Fact]
    public void F12_every_variable_of_both_templates_is_given_by_the_row_and_a_blank_is_named()
    {
        var values = TemplateRender.Values(Lead('A'), Eight);
        foreach (var template in CampaignTemplate.All)
        {
            Assert.All(TemplateFill.Variables(template.Text), variable => Assert.True(values.ContainsKey(variable), $"{template.Id}-{template.Version} names {{{variable}}}"));
        }
        Assert.Equal(["A", "B"], CampaignTemplate.All.Where(t => t.Version == CampaignTemplate.Current).Select(t => t.Id).Order());
        var blank = TemplateRender.Render(CampaignTemplate.For('B'), TemplateRender.Values(Lead(edit: b => b.FirstName = " "), Eight));
        Assert.False(blank.Complete);
        Assert.Equal(["FirstName"], blank.Blanks);
    }

    [Theory]
    [InlineData("shadow", CampaignMode.Shadow)]
    [InlineData(" ON ", CampaignMode.On)]
    [InlineData("off", CampaignMode.Off)]
    [InlineData("yes", CampaignMode.Off)]
    [InlineData(null, CampaignMode.Off)]
    public void F4_the_switch_is_on_or_shadow_and_anything_else_is_off(string? value, CampaignMode mode) =>
        Assert.Equal(mode, CampaignSendTimer.Mode(value));

    [Fact]
    public void F7_the_eight_o_clock_hour_is_found_on_both_sides_of_the_clock_change()
    {
        Assert.True(CentralClock.IsCampaignHour(new DateTimeOffset(2026, 10, 31, 13, 0, 0, TimeSpan.Zero)));
        Assert.False(CentralClock.IsCampaignHour(new DateTimeOffset(2026, 11, 1, 13, 0, 0, TimeSpan.Zero)));
        Assert.True(CentralClock.IsCampaignHour(new DateTimeOffset(2026, 11, 1, 14, 0, 0, TimeSpan.Zero)));
        Assert.Single(Enumerable.Range(0, 24).Select(h => new DateTimeOffset(2026, 11, 3, h, 0, 0, TimeSpan.Zero)), at => CentralClock.IsCampaignHour(at));
    }

    private static (CampaignSend Send, MemoryCampaign Db, CountingSender Mail) Pass(IEnumerable<LeadRecord> leads, string? master = null, DateTimeOffset? now = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "campaign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, MergePaths.Master), master ?? "\"Count\",Person,Company,Communication,Date,Phone/Email,Notes\r\n");
        var db = new MemoryCampaign([.. leads]);
        var mail = new CountingSender();
        return (new CampaignSend(db, db, mail, new FolderMergeStore(root), new FixedTime(now ?? Eight)) { Spacing = TimeSpan.Zero }, db, mail);
    }

    [Fact]
    public async Task F4_shadow_writes_the_would_sends_and_never_reaches_the_mail_port()
    {
        var (send, db, mail) = Pass(Enumerable.Range(0, 12).Select(i => Lead(company: $"Shop {i}", key: $"shop{i}.com")));

        var result = await send.RunAsync(CampaignMode.Shadow, CancellationToken.None);

        Assert.Equal(0, mail.Calls);
        Assert.Equal(10, result.Written);
        Assert.All(db.Emails, e => Assert.True(e.Shadow));
        Assert.All(db.Emails, e => Assert.Null(e.GmailMessageId));
        Assert.Equal("planned", result.Came);
        Assert.StartsWith("shadow: 12 open, 12 eligible, 10 written of 10 left today", result.Line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task F4_on_sends_ten_of_eleven_and_a_second_firing_the_same_day_sends_nothing()
    {
        var (send, db, mail) = Pass(Enumerable.Range(0, 11).Select(i => Lead(company: $"Shop {i}", key: $"shop{i}.com")));
        foreach (int day in new[] { 5, 4, 3 })
        {
            db.Emails.Add(Logged(day, shadow: false));
        }

        var first = await send.RunAsync(CampaignMode.On, CancellationToken.None);
        var second = await send.RunAsync(CampaignMode.On, CancellationToken.None);

        Assert.Equal((10, 10), (first.Written, mail.Calls));
        Assert.Equal(0, second.Written);
        Assert.Equal(10, db.Emails.Count(e => !e.Shadow && e.CentralDay == new DateOnly(2026, 10, 24)));
        Assert.All(db.Emails.Where(e => e.CentralDay == new DateOnly(2026, 10, 24)), e => Assert.Equal("m-" + e.LeadId, e.GmailMessageId));
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    public async Task The_first_three_days_of_on_send_three_then_five_then_ten(int daysBefore, int expected)
    {
        var (send, db, mail) = Pass(Enumerable.Range(0, 11).Select(i => Lead(company: $"Shop {i}", key: $"shop{i}.com")));
        for (int day = 1; day <= daysBefore; day++)
        {
            db.Emails.Add(Logged(day, shadow: false));
        }
        db.Emails.Add(Logged(1, shadow: true));

        var result = await send.RunAsync(CampaignMode.On, CancellationToken.None);

        Assert.Equal(expected, result.Written);
        Assert.Equal(expected, mail.Calls);
    }

    private static EmailRecord Logged(int daysAgo, bool shadow) => new()
    {
        LeadId = 9000 + daysAgo,
        Key = $"old{daysAgo}.com",
        Company = $"Old {daysAgo}",
        To = $"x@old{daysAgo}.com",
        TemplateId = "B",
        TemplateVersion = "v1",
        Subject = "s",
        BodyText = "t",
        BodyHtml = "h",
        SentAt = Eight.AddDays(-daysAgo),
        CentralDay = new DateOnly(2026, 10, 24).AddDays(-daysAgo),
        Touch = 1,
        Shadow = shadow,
    };

    [Fact]
    public async Task The_rules_hold_leads_with_their_reasons_and_one_company_gets_one_email()
    {
        var master = "\"Count\",Person,Company,Communication,Date,Phone/Email,Notes\r\n3001,,Threaded,LinkedIn,10/20/2026,,INBOUND: a manager wrote back\r\n";
        var leads = new[]
        {
            Lead(company: "Storee Construction", key: "storee.com"),
            Lead(company: "Quiet", key: "quiet.com", edit: b => b.ContactEmail = null),
            Lead(company: "Sourceless", key: "sourceless.com", edit: b => b.EmailSource = null),
            Lead(company: "Personal", key: "personal.com", edit: b => b.ContactEmail = "pat@gmail.com"),
            Lead(company: "Unchecked", key: "unchecked.com", edit: b => b.MxChecked = null),
            Lead(company: "Threaded", key: "threaded.com"),
            Lead(company: "Stopped", key: "stopped.com", edit: b => b.Company = b.Company with { DoNotEmail = true }),
            Lead(company: "Wordy", key: "wordy.com", edit: b => b.WhatTheyDo = "builds robust dental software"),
            Lead('A', company: "Yesterday", key: "yesterday.com", edit: b => b.AppliedAt = Eight.AddDays(-1)),
            Lead('A', company: "Old", key: "old.com", edit: b => b.AppliedAt = Eight.AddDays(-9)),
            Lead(company: "Twice", key: "twice.com"),
            Lead(company: "Twice", key: "twice.com"),
            Lead('A', company: "Today", key: "today.com"),
        };
        var (send, db, mail) = Pass(leads, master);

        var result = await send.RunAsync(CampaignMode.Shadow, CancellationToken.None);

        Assert.Equal(["today.com", "twice.com"], db.Emails.Select(e => e.Key).Order());
        Assert.Equal(1, result.Held["former employer"]);
        Assert.Equal(1, result.Held["no address"]);
        Assert.Equal(1, result.Held["no source"]);
        Assert.Equal(1, result.Held["personal domain (gmail.com)"]);
        Assert.Equal(1, result.Held["MX not checked"]);
        Assert.Equal(1, result.Held["live thread, Count 3001"]);
        Assert.Equal(1, result.Held["do not email"]);
        Assert.Equal(1, result.Held["WhatTheyDo"]);
        Assert.Equal(1, result.Held["template A v1 says applied today"]);
        Assert.Equal(1, result.Held["applied 2026-10-15, outside 7 days"]);
        Assert.Equal(1, result.Held["30-day rule"]);
        Assert.Equal(["WhatTheyDo: a tell (robust)"], db.Blocked.Values);
        Assert.Equal(0, mail.Calls);
    }

    [Fact]
    public async Task Off_reads_nothing_and_writes_nothing()
    {
        var (send, db, _) = Pass([Lead()]);
        var result = await send.RunAsync(CampaignMode.Off, CancellationToken.None);
        Assert.Equal(("unchanged", "off", 0), (result.Came, result.Line, db.Reads));
    }

    [Fact]
    public void The_message_is_two_bodies_in_base64_and_refuses_a_second_address()
    {
        string mime = GmailSender.Mime(new OutgoingMail("pat@acme.com", "Remote .NET at Acme", "Hi\nthere", "<div>Hi</div>"), "b1");
        Assert.StartsWith("To: pat@acme.com\r\nSubject: Remote .NET at Acme\r\nMIME-Version: 1.0\r\nContent-Type: multipart/alternative; boundary=\"b1\"\r\n", mime, StringComparison.Ordinal);
        Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("Hi\r\nthere")), mime, StringComparison.Ordinal);
        Assert.Contains("Content-Type: text/html; charset=\"UTF-8\"", mime, StringComparison.Ordinal);
        Assert.EndsWith("--b1--\r\n", mime, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => GmailSender.Mime(new OutgoingMail("pat@acme.com\r\nBcc: x@y.com", "s", "t", "h"), "b"));
        Assert.Throws<ArgumentException>(() => GmailSender.Mime(new OutgoingMail("a@b.com, c@d.com", "s", "t", "h"), "b"));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CountingSender : IMailSender
    {
        public int Calls { get; private set; }

        public Task<SentMail> SendAsync(OutgoingMail mail, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new SentMail("pending", "t-" + Calls, $"<{Calls}@mail.gmail.com>"));
        }
    }

    private sealed class MemoryCampaign(List<LeadRecord> leads) : ILeadStore, IEmailLog
    {
        public List<EmailRecord> Emails { get; } = [];

        public Dictionary<long, string> Blocked { get; } = [];

        public int Reads { get; private set; }

        public Task<IReadOnlyList<LeadRecord>> OpenAsync(CancellationToken ct)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<LeadRecord>>([.. leads.Where(l => !Blocked.ContainsKey(l.Id))]);
        }

        public Task BlockAsync(long leadId, string reason, CancellationToken ct)
        {
            Blocked[leadId] = reason;
            return Task.CompletedTask;
        }

        public Task MarkUnverifiedAsync(long leadId, string reason, CancellationToken ct) => Task.CompletedTask;

        public Task SaveAsync(int companyId, SpoolLead lead, bool? mx, int strength, string inputsHash, string? blocked, string run, DateTimeOffset at, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<LoggedEmail>> SinceAsync(DateOnly centralDay, CancellationToken ct)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<LoggedEmail>>([.. Emails.Where(e => e.CentralDay >= centralDay).Select(e => new LoggedEmail(e.LeadId, e.Key, e.ParentKey, e.Company, e.CentralDay, e.Shadow))]);
        }

        public Task<int> CountAsync(DateOnly centralDay, bool shadow, CancellationToken ct) => Task.FromResult(Emails.Count(e => e.CentralDay == centralDay && e.Shadow == shadow));

        public Task<long> WriteAsync(EmailRecord email, CancellationToken ct)
        {
            Emails.Add(email.GmailMessageId is null ? email : email with { GmailMessageId = "m-" + email.LeadId });
            return Task.FromResult((long)Emails.Count);
        }

        public Task<IReadOnlyList<SentThread>> SentThreadsAsync(DateOnly centralDay, CancellationToken ct) => Task.FromResult<IReadOnlyList<SentThread>>([]);

        public Task MarkBouncedAsync(long emailId, string reason, CancellationToken ct) => Task.CompletedTask;

        public Task MarkReplyAsync(string gmailThreadId, DateTimeOffset at, CancellationToken ct) => Task.CompletedTask;
    }
}

/// <summary>A mutable copy of a lead for the tests to change one field at a time.</summary>
internal sealed class LeadRecordBuilder
{
    public long Id { get; set; }
    public char Track { get; set; }
    public required CompanyRecord Company { get; set; }
    public string CompanyName { get; set; } = "";
    public string? RoleTitle { get; set; }
    public string WhatTheyDo { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string ContactTitle { get; set; } = "";
    public string? ContactEmail { get; set; }
    public string? EmailSource { get; set; }
    public bool? MxChecked { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public DateOnly FirstSeen { get; set; }
    public string? RefCode { get; set; }
    public string? ResumeLink { get; set; }
    public string? SiteLink { get; set; }
    public string? GitHubLink { get; set; }
    public string? LinkedInLink { get; set; }
    public string? AboutLink { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public LeadRecord Build() => new()
    {
        Id = Id,
        Company = Company,
        Track = Track,
        CompanyName = CompanyName,
        RoleTitle = RoleTitle,
        WhatTheyDo = WhatTheyDo,
        FirstName = FirstName,
        ContactName = ContactName,
        ContactTitle = ContactTitle,
        ContactEmail = ContactEmail,
        EmailSource = EmailSource,
        MxChecked = MxChecked,
        AppliedAt = AppliedAt,
        FirstSeen = FirstSeen,
        RefCode = RefCode,
        ResumeLink = ResumeLink,
        SiteLink = SiteLink,
        GitHubLink = GitHubLink,
        LinkedInLink = LinkedInLink,
        AboutLink = AboutLink,
        CreatedAt = CreatedAt,
    };
}
