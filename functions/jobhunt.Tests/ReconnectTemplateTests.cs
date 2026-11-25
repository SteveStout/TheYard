using JobHunt.Domain.Shared.Companies;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// RECONNECT-v1 against Steve's own shape of 17 November, byte for byte from golden files (both second questions, the
/// plain part and the HTML part); the checks a reconnect must pass; the second question read from the tracker; one
/// test a guard; and the code minted the make_ref.py way.
/// </summary>
public sealed class ReconnectTemplateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static string Golden(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "functions", "jobhunt.Tests", "Fixtures", "reconnect", name);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }
        throw new InvalidOperationException($"the golden file {name} is not found");
    }

    [Theory]
    [InlineData(ReconnectQuestion.Status, "status")]
    [InlineData(ReconnectQuestion.Remote, "remote")]
    public void RECONNECT_v1_renders_Steve_s_shape_byte_for_byte(ReconnectQuestion question, string file)
    {
        var mail = ReconnectTemplate.Render("Pat", "the platform consolidation on .NET 10.", "RE: Platform role", question, "abc123");
        Assert.Equal(Golden(file + ".txt"), mail.Text);
        Assert.Equal(Golden(file + ".html"), mail.Html);
        Assert.Equal("Re: Platform role", mail.Subject);
        Assert.Empty(ReconnectTemplate.Check(mail, "abc123"));
        Assert.Equal("RECONNECT-v1", ReconnectTemplate.Version);
    }

    [Fact]
    public void The_About_link_opens_the_author_page_never_the_page_for_crawlers()
    {
        var mail = ReconnectTemplate.Render("Pat", "the role", "Role", ReconnectQuestion.Remote, "abc123");
        Assert.Equal("https://theyard.stevenstout.biz/?doc=author&r=abc123", ReconnectTemplate.Links("abc123").Single(link => link.Label == "About").Url);
        Assert.DoesNotContain("/about", mail.Text + mail.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void No_greeting_word_no_blog_line_no_call_ask_and_no_address_printed_in_the_HTML()
    {
        var mail = ReconnectTemplate.Render("Pat", "the role", "Role", ReconnectQuestion.Remote, "abc123");
        Assert.DoesNotContain("Hi ", mail.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("wrote up", mail.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("15 minutes", mail.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(">http", mail.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("linkedin.com/in/", mail.Text + mail.Html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "the role", "blank")]
    [InlineData("Pat", "", "blank")]
    [InlineData("Pat", "whether it ships? and when", "question")]
    [InlineData("Pat", "the role \u2014 and more", "dash")]
    public void A_reconnect_that_fails_a_check_says_which(string first, string topic, string failed)
    {
        var mail = ReconnectTemplate.Render(first, topic, "Role", ReconnectQuestion.Remote, "abc123");
        Assert.Contains(failed, ReconnectTemplate.Check(mail, "abc123"));
    }

    [Fact]
    public void A_link_without_the_send_s_code_fails()
    {
        var mail = ReconnectTemplate.Render("Pat", "the role", "Role", ReconnectQuestion.Remote, "abc123");
        Assert.Contains("links", ReconnectTemplate.Check(mail, "zzz999"));
    }

    private static string[] Tracker(int count, string company, string notes, string date = "11/10/2026") => [count.ToString(System.Globalization.CultureInfo.InvariantCulture), "Pat Lee", company, "Email", date, "", notes];

    [Fact]
    public void The_status_question_only_while_an_application_is_open()
    {
        var day = new DateOnly(2026, 11, 23);
        Assert.Equal(ReconnectQuestion.Status, ReconnectTemplate.QuestionFor("Acme", [Tracker(10, "Acme", "SUBMITTED via Greenhouse")], day));
        Assert.Equal(ReconnectQuestion.Remote, ReconnectTemplate.QuestionFor("Acme", [Tracker(10, "Acme", "SUBMITTED via Greenhouse"), Tracker(11, "Acme", "REJECTION, generic")], day));
        Assert.Equal(ReconnectQuestion.Remote, ReconnectTemplate.QuestionFor("Acme", [Tracker(10, "Acme", "SUBMITTED via Greenhouse", "8/1/2026")], day));
        Assert.Equal(ReconnectQuestion.Remote, ReconnectTemplate.QuestionFor(null, [Tracker(10, "Acme", "SUBMITTED via Greenhouse")], day));
    }

    private static Conversation Row(string email = "pat@acme.com") => new()
    {
        Id = 1,
        PersonKey = email,
        FirstName = "Pat",
        FullName = "Pat Lee",
        Company = "Acme",
        CompanyKey = "acme.com",
        Email = email,
        Source = "gmail",
        ThreadIds = ["t-1"],
        LastChannel = Channel.Email,
        TopicLine = "the role",
        LastInboundAt = Now.AddDays(-26),
        LastOutboundAt = Now.AddDays(-25),
    };

    private static ThreadSummary Thread(params ThreadMessage[] messages) => new("t-1", "Role", messages);

    private static readonly ThreadSummary Quiet = Thread(
        new ThreadMessage("Pat Lee <pat@acme.com>", Now.AddDays(-26), false, false),
        new ThreadMessage("Steven Stout <stevenstout11@gmail.com>", Now.AddDays(-25), true, false));

    private static ReconnectContext Context(ThreadSummary? thread = null, Blacklist? list = null, int company = 0, int pass = 0) =>
        new(thread ?? Quiet, list ?? Blacklist.None, company, pass, Now);

    [Fact]
    public void A_quiet_row_passes_every_guard() => Assert.Null(ReconnectGuards.Hold(Row(), Context()));

    [Fact]
    public void Guard_blacklist() =>
        Assert.Equal("blacklist", ReconnectGuards.Hold(Row(), Context(list: Blacklist.Read("| Added | Person | Address | Company | Steve's words | Why |\n|---|---|---|---|---|---|\n| 2026-11-01 | ALL OF ACME (company-wide) | any @acme.com | Acme | x | y |\n"))));

    [Fact]
    public void Guard_former_employer() => Assert.Equal("former employer", ReconnectGuards.Hold(Row("pat@cassinfo.com") with { Company = "Cass Information Systems" }, Context()));

    [Theory]
    [InlineData("no-reply@acme.com")]
    [InlineData("pat@hire.lever.co")]
    [InlineData("acme@myworkday.com")]
    [InlineData("jobs@greenhouse.io")]
    public void Guard_relay_or_no_reply(string address) => Assert.Equal("relay or no-reply", ReconnectGuards.Hold(Row(address), Context()));

    [Fact]
    public void Guard_reply_owed_when_the_person_wrote_last() =>
        Assert.Equal("reply owed", ReconnectGuards.Hold(Row(), Context(Thread(
            new ThreadMessage("Steven Stout <stevenstout11@gmail.com>", Now.AddDays(-26), true, false),
            new ThreadMessage("Pat Lee <pat@acme.com>", Now.AddDays(-25), false, false)))));

    [Fact]
    public void Guard_21_days_reads_a_hand_sent_reconnect_in_the_thread_before_any_pass_has()
    {
        var thread = Thread(
            new ThreadMessage("Pat Lee <pat@acme.com>", Now.AddDays(-30), false, false),
            new ThreadMessage("Steven Stout <stevenstout11@gmail.com>", Now.AddDays(-6), true, false));
        Assert.Equal("within 21 days", ReconnectGuards.Hold(Row(), Context(thread)));
        Assert.Equal("within 21 days", ReconnectGuards.Hold(Row() with { DraftedAt = Now.AddDays(-7) }, Context()));
    }

    [Fact]
    public void Guard_company_cap() => Assert.Equal("company cap", ReconnectGuards.Hold(Row(), Context(company: 3)));

    [Fact]
    public void Guard_pass_cap() => Assert.Equal("pass cap", ReconnectGuards.Hold(Row(), Context(pass: 15)));

    [Fact]
    public void A_code_is_six_of_make_ref_s_alphabet_and_never_one_already_taken()
    {
        var taken = new HashSet<string>(RefCodes.In("code,person\nabc123,Pat\n\"def456\",Sam\n"), StringComparer.Ordinal);
        Assert.Equal(["abc123", "def456"], taken.Order(StringComparer.Ordinal));
        string code = RefCodes.New(taken);
        Assert.Equal(6, code.Length);
        Assert.All(code, c => Assert.Contains(c, RefCodes.Alphabet));
        Assert.Contains(code, taken);
        Assert.Equal(3, taken.Count);
    }
}
