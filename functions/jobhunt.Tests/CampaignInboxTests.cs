using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The campaign's side of the inbox pass (ADR-118, Decision 4; the review, F11): a delivery failure read off the
/// body Gmail writes (the fixture is a real failure's shape, 2026-07-28, with the address made up), the stop
/// phrases above the quoted original, the trip row and its reading, and the part over memory stores: a failure
/// in a sent thread bounces the email once and marks the address, two in a Central day trip the switch, a reply
/// marks the email answered, and a stop phrase marks the company and lists the thread.
/// </summary>
public sealed class CampaignInboxTests
{
    private static string Dsn()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "functions", "jobhunt.Tests", "Fixtures", "campaign", "dsn-address-not-found.txt");
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }
        throw new InvalidOperationException("the DSN fixture is not found");
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 24, 15, 20, 0, TimeSpan.Zero);

    private static InboxMessage Failure(string id, string thread, DateTimeOffset at) =>
        new(id, thread, "Mail Delivery Subsystem <mailer-daemon@googlemail.com>", "Delivery Status Notification (Failure)", at, Dsn().Replace('\n', ' ')) { Lines = Dsn() };

    [Fact]
    public void F11_a_failure_names_the_address_the_status_and_the_reason_and_nothing_else_is_one()
    {
        var bounce = DeliveryFailure.Read(Failure("m1", "t1", Now));
        Assert.Equal(new Bounce("pat.lee@acmedental.com", "5.1.3", "Address not found (5.1.3)"), bounce);
        Assert.Null(DeliveryFailure.Read(new InboxMessage("m2", "t1", "Pat <pat@acme.com>", "Delivery Status Notification (Failure)", Now, "") { Lines = Dsn() }));
        Assert.Null(DeliveryFailure.Read(Failure("m3", "t1", Now) with { Subject = "Delivery Status Notification (Delay)" }));
    }

    [Theory]
    [InlineData("Thanks, but we're not interested right now.", "not interested")]
    [InlineData("Please REMOVE ME from your list", "remove me")]
    [InlineData("No thanks.\n\nOn Sat, Oct 24, 2026 at 8:00 AM Steve wrote:\n> Good morning!", "no thanks")]
    [InlineData("Happy to talk Tuesday.\n\nOn Sat, Oct 24, 2026 at 8:00 AM Steve wrote:\n> unsubscribe", null)]
    [InlineData("Happy to talk.\n> not interested", null)]
    [InlineData("Please don’t contact me again", "don't contact")]
    public void F11_a_stop_phrase_counts_only_in_the_reply_s_own_lines(string reply, string? phrase) =>
        Assert.Equal(phrase, StopPhrases.Find(reply));

    [Fact]
    public void F11_the_newest_switch_row_decides_and_only_shadow_trips()
    {
        Assert.False(BounceTrip.Tripped(""));
        Assert.True(BounceTrip.Tripped(BounceTrip.Row(new DateOnly(2026, 10, 24), 2) + "\n"));
        Assert.False(BounceTrip.Tripped(BounceTrip.Row(new DateOnly(2026, 10, 24), 2) + "\n2026-10-25 on Steve checked the two addresses\n"));
        Assert.StartsWith("2026-10-24 shadow 2 bounces", BounceTrip.Row(new DateOnly(2026, 10, 24), 2), StringComparison.Ordinal);
    }

    private static SentThread Sent(long id, string thread, string to = "pat.lee@acmedental.com", int daysAgo = 1, bool bounced = false) =>
        new(id, 100 + id, "acmedental.com", to, thread, new DateOnly(2026, 10, 24).AddDays(-daysAgo), bounced, null);

    private static (CampaignInbox Part, FakeInbox Inbox, Book Db, FolderMergeStore Store, string Root) Part(IEnumerable<SentThread> threads)
    {
        string root = Path.Combine(Path.GetTempPath(), "campaign-inbox-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var inbox = new FakeInbox();
        var db = new Book([.. threads]);
        var store = new FolderMergeStore(root);
        return (new CampaignInbox(inbox, db, db, db, store, new FixedTime(Now)), inbox, db, store, root);
    }

    [Fact]
    public async Task F11_two_failures_today_bounce_their_emails_once_and_trip_the_switch()
    {
        var (part, inbox, db, _, root) = Part([Sent(1, "t1"), Sent(2, "t2", "sam@acmedental.com"), Sent(3, "t3", bounced: true)]);
        inbox.Add(Failure("f1", "t1", Now.AddHours(-1)));
        inbox.Add(Failure("f2", "t2", Now.AddHours(-2)) with { Lines = Dsn().Replace("pat.lee@", "sam@", StringComparison.Ordinal) });
        inbox.Add(Failure("f3", "t9", Now));

        string line = await part.RunAsync(false, CancellationToken.None);
        string again = await part.RunAsync(false, CancellationToken.None);

        Assert.Equal([1L, 2L], db.Bounced);
        Assert.Equal([101L, 102L], db.Unverified);
        Assert.StartsWith("3 sent threads, 3 failures read, 2 bounced, 0 replies, 0 stops (replies read on the hour) | BLOCKED: 2 bounces today", line, StringComparison.Ordinal);
        Assert.Contains("0 bounced", again, StringComparison.Ordinal);
        string switchLog = File.ReadAllText(Path.Combine(root, "campaign", "switch.log"));
        Assert.Single(switchLog.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.True(BounceTrip.Tripped(switchLog));
    }

    [Fact]
    public async Task F11_one_failure_today_and_one_yesterday_do_not_trip_it()
    {
        var (part, inbox, _, _, root) = Part([Sent(1, "t1"), Sent(2, "t2", "sam@acmedental.com")]);
        inbox.Add(Failure("f1", "t1", Now));
        inbox.Add(Failure("f2", "t2", Now.AddDays(-1)) with { Lines = Dsn().Replace("pat.lee@", "sam@", StringComparison.Ordinal) });

        string line = await part.RunAsync(false, CancellationToken.None);

        Assert.DoesNotContain("BLOCKED", line, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "campaign", "switch.log")));
    }

    [Fact]
    public async Task F11_a_reply_marks_the_email_and_a_stop_marks_the_company_and_lists_the_thread()
    {
        var (part, inbox, db, _, root) = Part([Sent(1, "t1"), Sent(2, "t2", "sam@acmedental.com"), Sent(3, "t3", "lee@acmedental.com")]);
        inbox.Senders["t1"] = ["Steve <me@gmail.com>", "Pat Lee <pat.lee@acmedental.com>"];
        inbox.Senders["t2"] = ["Steve <me@gmail.com>", "Sam <sam@acmedental.com>"];
        inbox.Senders["t3"] = ["Steve <me@gmail.com>"];
        inbox.Add(new InboxMessage("r1", "t1", "Pat Lee <pat.lee@acmedental.com>", "Re: Remote", Now, "") { Lines = "Happy to talk Tuesday." });
        inbox.Add(new InboxMessage("r2", "t2", "Sam <sam@acmedental.com>", "Re: Remote", Now, "") { Lines = "We're not interested, thanks.\n\nOn Sat Steve wrote:\n> hi" });

        string line = await part.RunAsync(true, CancellationToken.None);

        Assert.Equal(["t1", "t2"], db.Replied);
        Assert.Equal([7], db.DoNotEmail);
        Assert.Contains("2 replies, 1 stops", line, StringComparison.Ordinal);
        Assert.Contains("t2 not interested", File.ReadAllText(Path.Combine(root, "campaign", "stop-threads.log")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_sent_reads_nothing()
    {
        var (part, inbox, _, _, _) = Part([]);
        Assert.Equal("0 sent threads, nothing to read", await part.RunAsync(true, CancellationToken.None));
        Assert.Equal(0, inbox.Finds);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeInbox : IInbox
    {
        private readonly List<InboxMessage> _messages = [];

        public Dictionary<string, IReadOnlyList<string>> Senders { get; } = [];

        public int Finds { get; private set; }

        public void Add(InboxMessage message) => _messages.Add(message);

        public Task<IReadOnlyList<string>> FindAsync(string query, int max, CancellationToken ct)
        {
            Finds++;
            IEnumerable<InboxMessage> found = query.StartsWith("from:mailer-daemon", StringComparison.Ordinal)
                ? _messages.Where(m => m.Address.StartsWith("mailer-daemon@", StringComparison.Ordinal))
                : _messages.Where(m => query.StartsWith("from:" + m.Address + " ", StringComparison.Ordinal));
            return Task.FromResult<IReadOnlyList<string>>([.. found.Take(max).Select(m => m.Id)]);
        }

        public Task<InboxMessage> ReadAsync(string id, CancellationToken ct) => Task.FromResult(_messages.Single(m => m.Id == id));

        public Task<IReadOnlyList<string>> ThreadSendersAsync(string threadId, CancellationToken ct) =>
            Task.FromResult(Senders.GetValueOrDefault(threadId) ?? (IReadOnlyList<string>)[]);

        public Task<bool> ArchiveAsync(string id, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class Book(List<SentThread> threads) : IEmailLog, ILeadStore, ICompanyBook
    {
        public List<long> Bounced { get; } = [];

        public List<long> Unverified { get; } = [];

        public List<string> Replied { get; } = [];

        public List<int> DoNotEmail { get; } = [];

        public Task<IReadOnlyList<SentThread>> SentThreadsAsync(DateOnly centralDay, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SentThread>>([.. threads.Select(t => t with { Bounced = t.Bounced || Bounced.Contains(t.EmailId) })]);

        public Task MarkBouncedAsync(long emailId, string reason, CancellationToken ct)
        {
            Bounced.Add(emailId);
            return Task.CompletedTask;
        }

        public Task MarkReplyAsync(string gmailThreadId, DateTimeOffset at, CancellationToken ct)
        {
            Replied.Add(gmailThreadId);
            return Task.CompletedTask;
        }

        public Task MarkUnverifiedAsync(long leadId, string reason, CancellationToken ct)
        {
            Unverified.Add(leadId);
            return Task.CompletedTask;
        }

        public Task<CompanyRecord?> FindAsync(string key, CancellationToken ct) =>
            Task.FromResult<CompanyRecord?>(new CompanyRecord(7, key, null, ["Acme Dental"], "Remote", null, null, 1, false));

        public Task MarkDoNotEmailAsync(int companyId, DateTimeOffset at, CancellationToken ct)
        {
            DoNotEmail.Add(companyId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LoggedEmail>> SinceAsync(DateOnly centralDay, CancellationToken ct) => Task.FromResult<IReadOnlyList<LoggedEmail>>([]);

        public Task<int> CountAsync(DateOnly centralDay, bool shadow, CancellationToken ct) => Task.FromResult(0);

        public Task<long> WriteAsync(EmailRecord email, CancellationToken ct) => Task.FromResult(0L);

        public Task<IReadOnlyList<LeadRecord>> OpenAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<LeadRecord>>([]);

        public Task BlockAsync(long leadId, string reason, CancellationToken ct) => Task.CompletedTask;

        public Task SaveAsync(int companyId, SpoolLead lead, bool? mx, int strength, string inputsHash, string? blocked, string run, DateTimeOffset at, CancellationToken ct) => Task.CompletedTask;

        public Task<int> SaveAsync(SpoolCompany company, string run, DateTimeOffset at, CancellationToken ct) => Task.FromResult(0);

        public Task<IReadOnlyList<BoardCompany>> BoardsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<BoardCompany>>([]);

        public Task MarkScannedAsync(int companyId, DateOnly? lastDotNetPostingAt, string? lastDotNetPostingTitle, DateTimeOffset at, CancellationToken ct) => Task.CompletedTask;
    }
}
