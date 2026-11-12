using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The network log's two Gmail adapters against a scripted Gmail: the window is paged and capped, a rate answer is
/// waited out, reads are spaced under the user's quota, an invite's calendar part is found in a nested part, the Ready label is listed before it is made and
/// made once, the draft goes into its thread, and a draft that is gone reads as gone. Every request is recorded, so
/// the tests also hold that no request ever reaches a send.
/// </summary>
public sealed class NetworkGmailTests
{
    private static readonly GoogleCredentials Google = new("id", "secret", "refresh");

    [Fact]
    public async Task The_window_is_paged_until_the_cap()
    {
        var gmail = new ScriptedGmail();
        gmail.On("GET", "/threads?q=", """{"threads":[{"id":"a"},{"id":"b"}],"nextPageToken":"p2"}""");
        gmail.On("GET", "/threads?q=", """{"threads":[{"id":"c"},{"id":"d"}],"nextPageToken":"p3"}""");
        var ids = await new GmailThreads(gmail, Google, TimeProvider.System).FindThreadIdsAsync(new ThreadSearch(2, 3), CancellationToken.None);
        Assert.Equal(["a", "b", "c", "d"], ids);
        Assert.Equal(2, gmail.Requests.Count(r => r.Contains("/threads?q=", StringComparison.Ordinal)));
        Assert.Contains("q=newer_than%3A2d%20-in%3Achats", gmail.Requests[1], StringComparison.Ordinal);
        Assert.Contains("pageToken=p2", gmail.Requests[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rate_answer_is_waited_out()
    {
        var gmail = new ScriptedGmail();
        gmail.On("GET", "/threads/t1?format=metadata", "", HttpStatusCode.TooManyRequests);
        gmail.On("GET", "/threads/t1?format=metadata", """{"id":"t1","messages":[]}""");
        var thread = await new GmailThreads(gmail, Google, TimeProvider.System).ReadSummaryAsync("t1", CancellationToken.None);
        Assert.Equal("t1", thread.ThreadId);
        Assert.Empty(thread.Messages);
    }

    [Fact]
    public async Task Gmail_s_quota_403_is_waited_out_and_a_refusal_fails_with_its_reason()
    {
        var gmail = new ScriptedGmail();
        gmail.On("GET", "/threads/t1?format=metadata", """{"error":{"code":403,"errors":[{"reason":"userRateLimitExceeded"}],"status":"PERMISSION_DENIED"}}""", HttpStatusCode.Forbidden);
        gmail.On("GET", "/threads/t1?format=metadata", """{"id":"t1","messages":[]}""");
        Assert.Equal("t1", (await new GmailThreads(gmail, Google, TimeProvider.System).ReadSummaryAsync("t1", CancellationToken.None)).ThreadId);

        var refused = new ScriptedGmail();
        refused.On("GET", "/threads/t2?format=metadata", """{"error":{"code":403,"errors":[{"reason":"insufficientPermissions"}],"status":"PERMISSION_DENIED"}}""", HttpStatusCode.Forbidden);
        var failed = await Assert.ThrowsAsync<HttpRequestException>(() => new GmailThreads(refused, Google, TimeProvider.System).ReadSummaryAsync("t2", CancellationToken.None));
        Assert.Equal(HttpStatusCode.Forbidden, failed.StatusCode);
        Assert.Contains("403 (insufficientPermissions) after 1 tries", failed.Message, StringComparison.Ordinal);
        Assert.Single(refused.Requests, r => r.Contains("/threads/t2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reads_at_once_are_spaced_under_the_user_s_quota()
    {
        var gmail = new ScriptedGmail();
        for (int i = 0; i < 6; i++)
        {
            gmail.On("GET", "/threads/t?format=metadata", """{"id":"t","messages":[]}""");
        }
        var reader = new GmailThreads(gmail, Google, TimeProvider.System);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => reader.ReadSummaryAsync("t", CancellationToken.None)));
        Assert.True(clock.Elapsed >= GmailThreads.Spacing * 5 - TimeSpan.FromMilliseconds(10), $"six reads took {clock.Elapsed.TotalMilliseconds} ms");
        Assert.True(TimeSpan.FromSeconds(1) / GmailThreads.Spacing * 10 <= 200, "a thread read costs 10 units and the user's quota is 250 a second");
    }

    [Fact]
    public void A_slot_waits_for_the_one_before_it_and_never_for_the_past()
    {
        Assert.Equal((0L, 150L), GmailThreads.Slot(100, 0, 50));
        Assert.Equal((30L, 180L), GmailThreads.Slot(100, 130, 50));
        Assert.Equal((0L, 550L), GmailThreads.Slot(500, 130, 50));
    }

    [Theory]
    [InlineData("""{"error":{"errors":[{"reason":"rateLimitExceeded"}]}}""", "rateLimitExceeded")]
    [InlineData("""{"error":{"status":"PERMISSION_DENIED"}}""", "PERMISSION_DENIED")]
    [InlineData("", null)]
    [InlineData("not json", null)]
    public void Gmail_s_reason_word_is_read_from_its_error_body(string body, string? reason) =>
        Assert.Equal(reason, GmailThreads.Reason(body));

    [Fact]
    public async Task An_invite_s_calendar_part_is_found_however_deep_it_sits()
    {
        var gmail = new ScriptedGmail();
        gmail.On("GET", "/threads/t1?format=full", """{"id":"t1","messages":[{"payload":{"mimeType":"multipart/mixed","parts":[{"mimeType":"multipart/alternative","parts":[{"mimeType":"text/plain"},{"mimeType":"text/calendar"}]}]}}]}""");
        gmail.On("GET", "/threads/t2?format=full", """{"id":"t2","messages":[{"payload":{"mimeType":"multipart/mixed","parts":[{"mimeType":"application/octet-stream","filename":"invite.ics"}]}}]}""");
        gmail.On("GET", "/threads/t3?format=full", """{"id":"t3","messages":[{"payload":{"mimeType":"text/plain"}}]}""");
        var reader = new GmailThreads(gmail, Google, TimeProvider.System);
        Assert.True(await reader.ReadHasCalendarPartAsync("t1", CancellationToken.None));
        Assert.True(await reader.ReadHasCalendarPartAsync("t2", CancellationToken.None));
        Assert.False(await reader.ReadHasCalendarPartAsync("t3", CancellationToken.None));
    }

    [Fact]
    public async Task The_label_is_listed_first_made_only_when_absent_and_cached()
    {
        var gmail = new ScriptedGmail();
        gmail.On("GET", "/labels", """{"labels":[{"id":"L1","name":"Follow-up"}]}""");
        gmail.On("POST", "/labels", """{"id":"L2","name":"Follow-up/Ready"}""");
        var drafts = new GmailDrafts(gmail, Google);
        Assert.Equal("L1", await drafts.EnsureLabelAsync("Follow-up", CancellationToken.None));
        Assert.Equal("L2", await drafts.EnsureLabelAsync("Follow-up/Ready", CancellationToken.None));
        Assert.Equal("L2", await drafts.EnsureLabelAsync("Follow-up/Ready", CancellationToken.None));
        Assert.Single(gmail.Bodies, b => b.Contains("Follow-up/Ready", StringComparison.Ordinal));
        Assert.Equal(1, gmail.Requests.Count(r => r.StartsWith("POST https://gmail", StringComparison.Ordinal)));
        Assert.Equal(1, gmail.Requests.Count(r => r.EndsWith("/labels", StringComparison.Ordinal) && r.StartsWith("GET", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task The_draft_goes_into_its_thread_and_is_labelled_and_read_back()
    {
        var gmail = new ScriptedGmail();
        gmail.On("POST", "/drafts", """{"id":"r-1","message":{"id":"m-9","threadId":"t9"}}""");
        gmail.On("POST", "/messages/m-9/modify", """{"id":"m-9"}""");
        gmail.On("GET", "/drafts/r-1?format=minimal", """{"id":"r-1"}""");
        gmail.On("GET", "/drafts/r-2?format=minimal", "", HttpStatusCode.NotFound);
        var drafts = new GmailDrafts(gmail, Google);
        var draft = new DraftMail("t9", "pat@acme.com", "Re: Platform role", "Hi Pat,", "<div>Hi Pat,</div>", "<a1@acme.com>", "<a1@acme.com>");

        var (id, message) = await drafts.CreateDraftAsync(draft, CancellationToken.None);
        await drafts.LabelAsync(message, "L2", CancellationToken.None);

        Assert.Equal(("r-1", "m-9"), (id, message));
        using var body = JsonDocument.Parse(gmail.Bodies.Single(b => b.Contains("\"raw\"", StringComparison.Ordinal)));
        Assert.Equal("t9", body.RootElement.GetProperty("message").GetProperty("threadId").GetString());
        string raw = body.RootElement.GetProperty("message").GetProperty("raw").GetString()!;
        string mime = Encoding.UTF8.GetString(Convert.FromBase64String(raw.Replace('-', '+').Replace('_', '/').PadRight(raw.Length + ((4 - (raw.Length % 4)) % 4), '=')));
        Assert.StartsWith("To: pat@acme.com\r\nSubject: Re: Platform role\r\nIn-Reply-To: <a1@acme.com>\r\n", mime, StringComparison.Ordinal);
        Assert.Contains("\"addLabelIds\":[\"L2\"]", gmail.Bodies.Single(b => b.Contains("addLabelIds", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.True(await drafts.DraftExistsAsync("r-1", CancellationToken.None));
        Assert.False(await drafts.DraftExistsAsync("r-2", CancellationToken.None));
        Assert.DoesNotContain(gmail.Requests, r => r.Contains("/send", StringComparison.Ordinal));
    }
}
