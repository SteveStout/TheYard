using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// Human mail never waits for 4 AM (PROMPT-JOBHUNT-OFF-THE-LAPTOP rung 5): who counts as a person, on the senders
/// in the inbox on 23 October 2026, and when the reply run may fire: between 7 AM and 9 PM Central, once in four
/// hours, and only for a thread a person wrote to that holds no draft.
/// </summary>
public sealed class HumanMailTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("human-mail-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("dchinnici@technologypartners.net", true)]
    [InlineData("paras@apetan.com", true)]
    [InlineData("sudheer.b@accionlabs.com", true)]
    [InlineData("inmail-hit-reply@linkedin.com", false)]
    [InlineData("messaging-digest-noreply@linkedin.com", false)]
    [InlineData("no.reply.alerts@chase.com", false)]
    [InlineData("notifications@github.com", false)]
    [InlineData("jobs-noreply@linkedin.com", false)]
    [InlineData("updates@marketing.remotehunter.com", false)]
    [InlineData("stevenstout11@gmail.com", false)]
    public void A_person_is_neither_Steve_nor_a_machine_nor_a_relay(string address, bool person) =>
        Assert.Equal(person, HumanMail.IsPerson(address));

    [Fact]
    public async Task A_person_waiting_on_a_thread_with_no_draft_fires_and_one_with_a_draft_does_not()
    {
        var inbox = new Inbox(("m1", "t1", "Dan <dchinnici@technologypartners.net>", false), ("m2", "t2", "Sudheer <sudheer.b@accionlabs.com>", true), ("m3", "t3", "LinkedIn <inmail-hit-reply@linkedin.com>", false));
        var human = new HumanMail(new FolderMergeStore(_root), inbox, At(13));

        var (fire, waiting, why) = await human.CheckAsync(CancellationToken.None);

        Assert.True(fire);
        Assert.Equal(1, waiting);
        Assert.Equal("waiting: dchinnici@technologypartners.net", why);
    }

    [Fact]
    public async Task Nothing_fires_outside_the_hours_or_inside_four_hours_of_the_last_fire()
    {
        var inbox = new Inbox(("m1", "t1", "dchinnici@technologypartners.net", false));
        var store = new FolderMergeStore(_root);

        Assert.False((await new HumanMail(store, inbox, At(5)).CheckAsync(CancellationToken.None)).Fire);
        Assert.False((await new HumanMail(store, inbox, At(21)).CheckAsync(CancellationToken.None)).Fire);

        await new HumanMail(store, inbox, At(9)).MarkFiredAsync(CancellationToken.None);
        var soon = await new HumanMail(store, inbox, At(12)).CheckAsync(CancellationToken.None);
        Assert.False(soon.Fire);
        Assert.StartsWith("fired ", soon.Why, StringComparison.Ordinal);
        Assert.True((await new HumanMail(store, inbox, At(13)).CheckAsync(CancellationToken.None)).Fire);
    }

    /// <summary>A clock at this hour Central on 23 October 2026 (CDT, five hours behind UTC).</summary>
    private static Fixed At(int centralHour) => new(new DateTimeOffset(2026, 10, 23, centralHour, 5, 0, TimeSpan.FromHours(-5)));

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }

    private sealed class Inbox(params (string Id, string Thread, string From, bool Draft)[] mails) : IInbox
    {
        public Task<IReadOnlyList<string>> FindAsync(string query, int max, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([.. mails.Select(m => m.Id)]);

        public Task<InboxMessage> ReadAsync(string id, CancellationToken ct)
        {
            var mail = mails.Single(m => m.Id == id);
            return Task.FromResult(new InboxMessage(mail.Id, mail.Thread, mail.From, "subject", DateTimeOffset.UnixEpoch, "text"));
        }

        public Task<IReadOnlyList<string>> ThreadSendersAsync(string threadId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([.. mails.Where(m => m.Thread == threadId).Select(m => m.From)]);

        public Task<bool> ArchiveAsync(string id, CancellationToken ct) => Task.FromResult(false);

        public Task<bool> ThreadHasDraftAsync(string threadId, CancellationToken ct) => Task.FromResult(mails.Any(m => m.Thread == threadId && m.Draft));
    }
}
