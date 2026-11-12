using System.Net;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// Every Gmail adapter through the one helper, against a scripted Gmail and a clock whose waits end at once: a 403 with
/// a rate reason and a 429 are waited out on doubling waits and then answer, a 403 that refuses fails on the first try
/// with Gmail's reason word, a rate answer that never clears fails after six tries with that word on the pass's FAILED
/// line, a POST is made again after a rate answer but a send is never made twice after a server error, and two adapters
/// share one slot clock.
/// </summary>
public sealed class GmailHttpTests
{
    private static readonly GoogleCredentials Google = new("id", "secret", "refresh");

    private const string Rate = """{"error":{"code":403,"errors":[{"reason":"rateLimitExceeded"}],"status":"PERMISSION_DENIED"}}""";

    private const string Message = """{"id":"m1","threadId":"t1","internalDate":"1794500000000","payload":{"headers":[{"name":"From","value":"Dana <dana@zephyrlabs.com>"}]}}""";

    [Fact]
    public async Task A_403_with_a_rate_reason_is_waited_out_and_the_read_then_answers()
    {
        var gmail = new ScriptedGmail();
        gmail.On("GET", "/messages/m1?format=full", Rate, HttpStatusCode.Forbidden);
        gmail.On("GET", "/messages/m1?format=full", Message);
        var clock = new InstantWaits();

        var message = await new GmailInbox(gmail, Google, clock).ReadAsync("m1", CancellationToken.None);

        Assert.Equal(("m1", "t1"), (message.Id, message.ThreadId));
        Assert.Equal([TimeSpan.FromSeconds(2)], clock.Waits);
    }

    [Fact]
    public async Task A_429_is_waited_out_and_the_list_then_answers_with_each_message_s_thread()
    {
        var gmail = new ScriptedGmail();
        gmail.On("GET", "/messages?q=", "", HttpStatusCode.TooManyRequests);
        gmail.On("GET", "/messages?q=", """{"messages":[{"id":"m1","threadId":"t1"},{"id":"m2","threadId":"t2"}]}""");
        var clock = new InstantWaits();

        var found = await new GmailInbox(gmail, Google, clock).FindInThreadsAsync(MailSearch.WholeInbox, 10, CancellationToken.None);

        Assert.Equal([("m1", "t1"), ("m2", "t2")], found);
        Assert.Equal([TimeSpan.FromSeconds(2)], clock.Waits);
    }

    [Theory]
    [InlineData("insufficientPermissions")]
    [InlineData("accessNotConfigured")]
    public async Task A_403_that_refuses_fails_on_the_first_try_with_its_reason(string reason)
    {
        var gmail = new ScriptedGmail();
        gmail.On("GET", "/messages/m1?format=full", $$$"""{"error":{"code":403,"errors":[{"reason":"{{{reason}}}"}]}}""", HttpStatusCode.Forbidden);
        var clock = new InstantWaits();

        var failed = await Assert.ThrowsAsync<HttpRequestException>(() => new GmailInbox(gmail, Google, clock).ReadAsync("m1", CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, failed.StatusCode);
        Assert.Equal($"Gmail answered 403 ({reason}) after 1 tries", failed.Message);
        Assert.Single(gmail.Requests, r => r.Contains("/messages/m1", StringComparison.Ordinal));
        Assert.Empty(clock.Waits);
    }

    [Fact]
    public async Task A_rate_answer_that_never_clears_fails_after_six_tries_and_the_FAILED_line_says_why()
    {
        var gmail = new ScriptedGmail();
        for (int i = 0; i <= GmailHttp.Retries; i++)
        {
            gmail.On("GET", "/threads/t1?format=metadata", Rate, HttpStatusCode.Forbidden);
        }
        var clock = new InstantWaits();

        var failed = await Assert.ThrowsAsync<HttpRequestException>(() => new GmailInbox(gmail, Google, clock).ThreadSendersAsync("t1", CancellationToken.None));

        Assert.Equal("FAILED: HttpRequestException: Gmail answered 403 (rateLimitExceeded) after 6 tries", AtsSweepTimer.Describe(failed));
        Assert.Equal([2, 4, 8, 16, 32], clock.Waits.Select(w => w.TotalSeconds));
    }

    [Fact]
    public async Task The_archive_s_label_change_is_made_again_after_a_rate_answer()
    {
        var gmail = new ScriptedGmail();
        gmail.On("POST", "/messages/m1/modify", Rate, HttpStatusCode.Forbidden);
        gmail.On("POST", "/messages/m1/modify", """{"id":"m1"}""");
        gmail.On("GET", "/messages/m1?format=minimal", """{"id":"m1","labelIds":["CATEGORY_UPDATES"]}""");

        Assert.True(await new GmailInbox(gmail, Google, new InstantWaits()).ArchiveAsync("m1", CancellationToken.None));
        Assert.Equal(2, gmail.Requests.Count(r => r.StartsWith("POST", StringComparison.Ordinal) && r.EndsWith("/modify", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_send_is_made_again_after_a_rate_answer_and_never_after_a_server_error()
    {
        var mail = new OutgoingMail("pat@acme.com", "Hello", "Hi Pat", "<p>Hi Pat</p>");
        var gmail = new ScriptedGmail();
        gmail.On("POST", "/messages/send", Rate, HttpStatusCode.Forbidden);
        gmail.On("POST", "/messages/send", """{"id":"s1","threadId":"t1"}""");
        gmail.On("GET", "/messages/s1?format=metadata", """{"id":"s1","payload":{"headers":[{"name":"Message-Id","value":"<s1@mail.gmail.com>"}]}}""");
        var sent = await new GmailSender(gmail, Google, new InstantWaits()).SendAsync(mail, CancellationToken.None);
        Assert.Equal(new SentMail("s1", "t1", "<s1@mail.gmail.com>"), sent);

        var broken = new ScriptedGmail();
        broken.On("POST", "/messages/send", """{"error":{"code":500,"status":"INTERNAL"}}""", HttpStatusCode.InternalServerError);
        var failed = await Assert.ThrowsAsync<HttpRequestException>(() => new GmailSender(broken, Google, new InstantWaits()).SendAsync(mail, CancellationToken.None));
        Assert.Equal("Gmail answered 500 (INTERNAL) after 1 tries", failed.Message);
        Assert.Single(broken.Requests, r => r.EndsWith("/messages/send", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_draft_is_made_again_after_a_rate_answer()
    {
        var gmail = new ScriptedGmail();
        gmail.On("POST", "/drafts", Rate, HttpStatusCode.Forbidden);
        gmail.On("POST", "/drafts", """{"id":"r-1","message":{"id":"m-9","threadId":"t9"}}""");
        var draft = new DraftMail("t9", "pat@acme.com", "Re: Platform role", "Hi Pat,", "<div>Hi Pat,</div>", null, null);

        Assert.Equal(("r-1", "m-9"), await new GmailDrafts(gmail, Google, new InstantWaits()).CreateDraftAsync(draft, CancellationToken.None));
        Assert.DoesNotContain(gmail.Requests, r => r.Contains("/send", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_adapters_share_one_slot_clock()
    {
        var gmail = new ScriptedGmail();
        for (int i = 0; i < 6; i++)
        {
            gmail.On("GET", "/threads/t?format=metadata", """{"id":"t","messages":[]}""");
        }
        var sweep = new GmailInbox(gmail, Google);
        var network = new GmailThreads(gmail, Google, TimeProvider.System);
        await sweep.ThreadSendersAsync("t", CancellationToken.None);
        await network.ReadSummaryAsync("t", CancellationToken.None);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(i => i % 2 == 0 ? sweep.ThreadSendersAsync("t", CancellationToken.None) : (Task)network.ReadSummaryAsync("t", CancellationToken.None)));

        Assert.True(clock.Elapsed >= GmailHttp.Spacing * 3 - TimeSpan.FromMilliseconds(10), $"four reads from two adapters took {clock.Elapsed.TotalMilliseconds} ms");
    }

    /// <summary>A clock whose waits end at once and are written down, so the doubling waits cost a test no time.</summary>
    private sealed class InstantWaits : TimeProvider
    {
        private readonly List<TimeSpan> _waits = [];

        public IReadOnlyList<TimeSpan> Waits
        {
            get
            {
                lock (_waits)
                {
                    return [.. _waits];
                }
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_waits)
            {
                _waits.Add(dueTime);
            }
            ThreadPool.UnsafeQueueUserWorkItem(_ => callback(state), null);
            return new Done();
        }

        private sealed class Done : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
