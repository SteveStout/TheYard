using System.Reflection;
using System.Text;
using System.Text.Json;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The ATS sweep (functions/JobHunt.Application/Features/Ats): the records run's rules for a known sender's mail as code,
/// what a message is, which company, whether a row exists for it to rest on, and the red lines as
/// tests: a person in the thread keeps it in the inbox, shadow archives nothing and writes no row,
/// and the inbox port can find, read, list a thread's senders and archive, and nothing else.
/// Every message and row here is made up.
/// </summary>
public sealed class AtsSweepTests
{
    private static readonly DateTimeOffset Afternoon = new(2026, 10, 22, 19, 30, 0, TimeSpan.Zero);

    private static InboxMessage Mail1(string text, string subject = "Thank you for applying to Acme", string from = "Acme Hiring Team <no-reply@us.greenhouse-mail.io>", DateTimeOffset? at = null, string id = "m1") =>
        new(id, "t-" + id, from, subject, at ?? Afternoon, text);

    private const string ReceiptText = "Thank you for applying to Acme. We have received your application for Senior Engineer and our team is reviewing it.";
    private const string RejectionText = "Thank you for your interest in Acme. After careful consideration we have decided to move forward with other candidates.";
    private const string ReasonText = "Thank you for your interest in Acme. Unfortunately we are not moving forward because the role requires candidates located in Canada.";

    private static string[] Applied(int count, string company = "Acme", string date = "10/21/2026") =>
        [count.ToString(System.Globalization.CultureInfo.InvariantCulture), "NA", company, "Greenhouse", date, "", "SUBMITTED by Claude: Senior Engineer (Greenhouse 123), remote US"];

    private static readonly string[] Machine = ["Acme Hiring Team <no-reply@us.greenhouse-mail.io>"];

    [Theory]
    [InlineData(ReceiptText, AtsKind.Receipt)]
    [InlineData(RejectionText, AtsKind.Rejection)]
    [InlineData(ReasonText, AtsKind.RejectionWithReason)]
    [InlineData("Thank you for applying. Please complete the assessment within 3 days.", AtsKind.Unknown)]
    [InlineData("Your weekly digest of jobs at Acme.", AtsKind.Unknown)]
    public void The_kind_is_read_from_the_text_and_anything_unclear_is_unknown(string text, AtsKind kind) =>
        Assert.Equal(kind, AtsRules.Kind(Mail1(text, subject: "Your application")));

    [Fact]
    public void A_receipt_for_an_application_on_the_master_is_archived_on_that_row_with_no_second_row()
    {
        var decision = AtsRules.Decide(Mail1(ReceiptText), Machine, [Applied(10)]);
        Assert.Equal((AtsAction.ArchiveOnly, "10", "Acme"), (decision.Action, decision.RestsOn, decision.Company));
        Assert.Null(decision.Row);
    }

    [Fact]
    public void A_generic_rejection_gets_a_row_naming_the_applications_then_is_archived()
    {
        var decision = AtsRules.Decide(Mail1(RejectionText, subject: "Your application to Acme"), Machine, [Applied(10), Applied(11, date: "10/1/2026")]);
        Assert.Equal(AtsAction.RowAndArchive, decision.Action);
        Assert.Equal(["NA", "Acme", "Email", "10/22/2026", "no-reply@us.greenhouse-mail.io"], decision.Row![..5]);
        Assert.StartsWith("REJECTION, generic, 10/22/2026 2:30 PM Central (Gmail msg m1", decision.Row[5], StringComparison.Ordinal);
        Assert.Contains("Counts 10, 11", decision.Row[5], StringComparison.Ordinal);
    }

    [Fact]
    public void A_rejection_already_logged_rests_on_its_row()
    {
        string[] logged = ["12", "NA", "Acme", "Email", "10/22/2026", "", "REJECTION, generic, logged by a lane"];
        var decision = AtsRules.Decide(Mail1(RejectionText), Machine, [Applied(10), logged]);
        Assert.Equal((AtsAction.ArchiveOnly, "12"), (decision.Action, decision.RestsOn));
    }

    [Fact]
    public void A_rejection_with_a_reason_is_logged_and_archived_because_processed_means_archived()
    {
        var decision = AtsRules.Decide(Mail1(ReasonText), Machine, [Applied(10)]);
        Assert.Equal(AtsAction.RowAndArchive, decision.Action);
        Assert.StartsWith("REJECTION WITH A REASON", decision.Row![5], StringComparison.Ordinal);
    }

    [Fact]
    public void A_message_a_row_names_by_its_gmail_id_rests_there()
    {
        string[] named = ["13", "NA", "Acme", "Email", "10/22/2026", "", "REJECTION, generic (Gmail msg m1, from no-reply)"];
        var decision = AtsRules.Decide(Mail1(RejectionText), Machine, [Applied(10), named]);
        Assert.Equal((AtsAction.ArchiveOnly, "13"), (decision.Action, decision.RestsOn));
    }

    [Fact]
    public void Interview_mail_a_row_names_stays_in_the_inbox()
    {
        string[] named = ["14", "NA", "Acme", "Email", "10/22/2026", "", "INTERVIEW scheduled (Gmail msg m1)"];
        var decision = AtsRules.Decide(Mail1("Hiring Manager Interview at Acme for Senior Software Engineer. Pick a time that works.", subject: "Hiring Manager Interview at Acme", id: "m1"), Machine, [Applied(10), named]);
        Assert.Equal(AtsAction.Leave, decision.Action);
        Assert.Contains("interview, scheduling and account mail stays", decision.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_person_in_the_thread_keeps_it_in_the_inbox()
    {
        var decision = AtsRules.Decide(Mail1(RejectionText), [.. Machine, "Jane Recruiter <jane@acme.example>"], [Applied(10)]);
        Assert.Equal(AtsAction.Leave, decision.Action);
        Assert.Contains("a person is in the thread", decision.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void Mail_from_before_this_search_is_archived_with_no_row()
    {
        var decision = AtsRules.Decide(Mail1(RejectionText, at: new DateTimeOffset(2025, 6, 1, 15, 0, 0, TimeSpan.Zero)), Machine, []);
        Assert.Equal(AtsAction.ArchiveOnly, decision.Action);
        Assert.Null(decision.Row);
    }

    [Fact]
    public void No_company_or_two_companies_keeps_it_in_the_inbox()
    {
        var none = AtsRules.Decide(Mail1(RejectionText.Replace("Acme", "Someone", StringComparison.Ordinal), subject: "Your application", from: "no-reply@greenhouse.io"), Machine, [Applied(10)]);
        Assert.Equal(AtsAction.Leave, none.Action);
        var two = AtsRules.Decide(Mail1(RejectionText + " Globex", subject: "Your application"), Machine, [Applied(10), Applied(11, "Globex")]);
        Assert.Equal(AtsAction.Leave, two.Action);
        Assert.Equal("Acme Health", AtsRules.Company(Mail1("Thanks from Acme Health", subject: "x", from: "no-reply@lever.co"), ["Acme", "Acme Health"]));
    }

    [Theory]
    [InlineData("no-reply@us.greenhouse-mail.io", true)]
    [InlineData("acme@myworkday.com", true)]
    [InlineData("noreply@acme.example", true)]
    [InlineData("careers@acme.example", true)]
    [InlineData("jane.doe@acme.example", false)]
    [InlineData("phil@ziprecruiter.com", false)]
    public void A_machine_sender_is_a_known_system_or_a_mailbox_nobody_answers(string address, bool machine) =>
        Assert.Equal(machine, Mail.IsMachine(address));

    [Fact]
    public async Task Shadow_writes_the_plan_and_archives_nothing_and_writes_no_row()
    {
        using var scratch = new Scratch();
        scratch.Master(TrackerCsvText.Line(Applied(10)) + "\r\n");
        var inbox = new MemoryInbox(Mail1(RejectionText, id: "m1"), Mail1(ReceiptText, id: "m2"));
        string line = await new AtsSweep(scratch.Store, inbox, new FixedTime(Afternoon)).RunAsync(false, CancellationToken.None);
        Assert.Empty(inbox.Archived);
        Assert.False(Directory.Exists(Path.Combine(scratch.Root, "inbox", "tracker-inbox")));
        Assert.StartsWith("SHADOW found 2: left 0, archive only 1, row and archive 1", line, StringComparison.Ordinal);
        Assert.Contains("m1\t2026-10-22 14:30", File.ReadAllText(Path.Combine(scratch.Root, "ats-sweep", "last-plan.tsv")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task On_drops_the_row_for_the_merger_then_archives_and_reads_back()
    {
        using var scratch = new Scratch();
        scratch.Master(TrackerCsvText.Line(Applied(10)) + "\r\n");
        var inbox = new MemoryInbox(Mail1(RejectionText, id: "m1"), Mail1(ReasonText, id: "m3"));
        string line = await new AtsSweep(scratch.Store, inbox, new FixedTime(Afternoon)).RunAsync(true, CancellationToken.None);
        Assert.Equal(["m1", "m3"], inbox.Archived);
        var rows = Directory.GetFiles(Path.Combine(scratch.Root, "inbox", "tracker-inbox"));
        Assert.Equal(2, rows.Length);
        Assert.All(rows, row => Assert.Matches(@"\d{8}-\d{6}Z-atssweep-m\d\.csv$", row));
        Assert.EndsWith("2 archived and read back, 0 failed", line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_inbox_port_can_find_read_list_senders_see_a_draft_and_archive_and_nothing_else()
    {
        var methods = typeof(IInbox).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).Order().ToList();
        Assert.Equal(["ArchiveAsync", "FindAsync", "ReadAsync", "ThreadHasDraftAsync", "ThreadSendersAsync"], methods);
    }

    [Fact]
    public void No_code_under_ats_can_send_draft_trash_or_delete()
    {
        string code = SliceCode(Root(), "Ats");
        foreach (string forbidden in new[] { "/send", "/drafts", "/trash", "batchDelete", "batchModify", "DeleteAsync", "HttpMethod.Delete", "addLabelIds" })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
        Assert.Contains("removeLabelIds = new[] { \"INBOX\" }", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_text_is_the_plain_part_else_the_html_without_markup()
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        using var html = JsonDocument.Parse($$$"""{"mimeType":"multipart/alternative","parts":[{"mimeType":"text/html","body":{"data":"{{{B64("<p>We have <b>received</b> your&nbsp;application</p><style>p{}</style>")}}}"}}]}""");
        Assert.Equal("We have received your application", GmailInbox.Text(html.RootElement));
        using var both = JsonDocument.Parse($$$"""{"mimeType":"multipart/alternative","parts":[{"mimeType":"text/plain","body":{"data":"{{{B64("plain words")}}}"}},{"mimeType":"text/html","body":{"data":"{{{B64("<p>html</p>")}}}"}}]}""");
        Assert.Equal("plain words", GmailInbox.Text(both.RootElement));
    }

    [Fact]
    public void A_job_alert_keeps_its_lines_so_its_blocks_can_be_read()
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        string alert = "Your job alert for engineering manager in United StatesManage your job alerts: https://www.linkedin.com/comm/jobs/alerts?x=1\r\n\r\nLead Engineer\r\nAcme   Corp\r\nRemote\r\nView job: https://www.linkedin.com/comm/jobs/view/123/?trk=x\r\n\r\n---------------------------------------------------------\r\n\r\nStaff Engineer\r\nGlobex\r\nUnited States\r\nView job: https://www.linkedin.com/comm/jobs/view/456/?trk=y\r\n";
        using var mail = JsonDocument.Parse($$$"""{"mimeType":"multipart/related","parts":[{"mimeType":"multipart/alternative","parts":[{"mimeType":"text/plain","body":{"data":"{{{B64(alert)}}}"}}]}]}""");
        string lines = GmailInbox.Lines(mail.RootElement);
        Assert.Contains("Acme Corp\nRemote\n", lines, StringComparison.Ordinal);
        var (leads, unread) = LinkedInAlert.Read(lines, "m9");
        Assert.Equal(0, unread);
        Assert.Equal(["Acme Corp", "Globex"], leads.Select(l => l.Company));
        // The one-line text the ATS rules read cannot be split into blocks, which is how the first live pass read none.
        Assert.Empty(LinkedInAlert.Read(GmailInbox.Text(mail.RootElement), "m9").Leads);
    }

    /// <summary>Every .cs file of one slice, read across the Function's three rings (ADR-117).</summary>
    private static string SliceCode(string root, string slice) => string.Join("\n", new[] { "JobHunt.Domain", "JobHunt.Application", "JobHunt.Infrastructure" }
        .Select(ring => Path.Combine(root, "functions", ring, "Features", slice))
        .Where(Directory.Exists)
        .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
        .Select(File.ReadAllText));

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "infra", "jobhunt.bicep")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("infra/jobhunt.bicep not found above " + AppContext.BaseDirectory);
    }

    /// <summary>An inbox in memory: every message from one known system, archives recorded.</summary>
    private sealed class MemoryInbox(params InboxMessage[] messages) : IInbox
    {
        public List<string> Archived { get; } = [];

        public Task<IReadOnlyList<string>> FindAsync(string query, int max, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([.. messages.Select(m => m.Id).Take(max)]);

        public Task<InboxMessage> ReadAsync(string id, CancellationToken ct) => Task.FromResult(messages.Single(m => m.Id == id));

        public Task<IReadOnlyList<string>> ThreadSendersAsync(string threadId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([.. messages.Where(m => m.ThreadId == threadId).Select(m => m.From)]);

        public Task<bool> ArchiveAsync(string id, CancellationToken ct)
        {
            Archived.Add(id);
            return Task.FromResult(true);
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
