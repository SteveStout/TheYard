using System.Text.Json;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The reply pass holds up when the share or the thread changes under it: an item whose publication stopped half way is
/// published on the next pass and never twice, a written draft is not placed once Steve has answered the thread or
/// another draft is in it, and the draft carries the reply headers Gmail needs to keep it in the person's thread.
/// </summary>
public sealed partial class ReplyDraftTests
{
    [Fact]
    public async Task An_item_whose_publication_stopped_is_published_on_the_next_pass_and_never_twice()
    {
        using var scratch = new Scratch();
        scratch.Master(Tracker);
        var flaky = new FailingReplace(scratch.Store);

        await Assert.ThrowsAsync<IOException>(() => new ReplyDrafts(flaky, Mailbox(), null, new Fixed(Now)).RunAsync(CancellationToken.None));
        Assert.True(scratch.Has("inbox-pass/reply-drafts/m1.md"));
        Assert.False(scratch.Has("dispatch/ready/12-replydraft-m1.md"));

        var next = await new ReplyDrafts(scratch.Store, Mailbox(), null, new Fixed(Now.AddMinutes(5))).RunAsync(CancellationToken.None);
        Assert.Equal(1, next.Queued);
        Assert.True(scratch.Has("dispatch/ready/12-replydraft-m1.md"));
        Assert.False(scratch.Has("dispatch/ready/.replydraft-m1.tmp"));

        File.Delete(Path.Combine(scratch.Root, "dispatch", "ready", "12-replydraft-m1.md"));
        var later = await new ReplyDrafts(scratch.Store, Mailbox(), null, new Fixed(Now.AddMinutes(10))).RunAsync(CancellationToken.None);
        Assert.Equal((0, 1), (later.Queued, later.Already));
        Assert.False(scratch.Has("dispatch/ready/12-replydraft-m1.md"));
    }

    [Fact]
    public async Task A_written_draft_is_not_placed_once_Steve_has_answered_or_another_draft_is_in_the_thread()
    {
        foreach (Mail1[] changed in new[]
        {
            [.. Mails(), new Mail1("m8", "t1", "Steve Stout <stevenstout11@gmail.com>", "Re: Staff Software Engineer - Web", Now.AddMinutes(7), "Thursday at 2?")],
            Mails().Select(m => m.Id == "m1" ? m with { Draft = true } : m).ToArray(),
        })
        {
            using var scratch = new Scratch();
            scratch.Master(Tracker);
            await new ReplyDrafts(scratch.Store, Mailbox(), null, new Fixed(Now)).RunAsync(CancellationToken.None);
            scratch.Write("inbox-pass/reply-drafts/m1.draft.md", "Dana,\n\nThursday works.\n\nSteve\n");
            var writer = new Drafts();

            var live = await new ReplyDrafts(scratch.Store, new Inbox(changed), writer, new Fixed(Now.AddMinutes(10))).RunAsync(CancellationToken.None);

            Assert.Equal(0, live.Placed);
            Assert.Empty(writer.Made);
            Assert.True(scratch.Has("inbox-pass/reply-drafts/m1.stale"));
            Assert.Contains("not placed the draft for m1", scratch.Text(ReplyDraft.Log), StringComparison.Ordinal);
            await new ReplyDrafts(scratch.Store, Mailbox(), writer, new Fixed(Now.AddMinutes(15))).RunAsync(CancellationToken.None);
            Assert.Empty(writer.Made);
        }
    }

    [Fact]
    public void The_Gmail_reader_keeps_the_message_s_id_and_its_references_for_the_reply()
    {
        using var json = JsonDocument.Parse("""
            {"id":"m1","threadId":"t1","internalDate":"1794412800000","payload":{"mimeType":"text/plain","headers":[
            {"name":"From","value":"Dana Whitfield <dana@zephyrlabs.com>"},{"name":"Subject","value":"Re: Staff Software Engineer - Web"},
            {"name":"Message-ID","value":"<dana-1@zephyrlabs.com>"},{"name":"References","value":"<steve-0@mail.gmail.com>"}],
            "body":{"data":"VGhhbmtz"}}}
            """);
        var message = GmailInbox.Message("m1", json.RootElement);
        Assert.Equal(("<dana-1@zephyrlabs.com>", "<steve-0@mail.gmail.com>"), (message.MessageId, message.References));
        Assert.Equal("t1", message.ThreadId);
    }

    /// <summary>The share, whose first rename fails, as a share that stops answering between the write and the rename.</summary>
    private sealed class FailingReplace(IMergeStore inner) : IMergeStore
    {
        private bool _failed;

        public Task<bool> ExistsAsync(string path, CancellationToken ct) => inner.ExistsAsync(path, ct);

        public Task<byte[]> ReadAsync(string path, CancellationToken ct) => inner.ReadAsync(path, ct);

        public Task<MergeEntry?> StatAsync(string path, CancellationToken ct) => inner.StatAsync(path, ct);

        public Task<IReadOnlyList<MergeEntry>> ListFilesAsync(string folder, CancellationToken ct) => inner.ListFilesAsync(folder, ct);

        public Task<IReadOnlyList<string>> ListFoldersAsync(string folder, CancellationToken ct) => inner.ListFoldersAsync(folder, ct);

        public Task EnsureFolderAsync(string folder, CancellationToken ct) => inner.EnsureFolderAsync(folder, ct);

        public Task WriteAsync(string path, byte[] bytes, CancellationToken ct) => inner.WriteAsync(path, bytes, ct);

        public Task MoveAsync(string path, string destination, CancellationToken ct) => inner.MoveAsync(path, destination, ct);

        public Task AppendAsync(string path, byte[] bytes, CancellationToken ct) => inner.AppendAsync(path, bytes, ct);

        public Task ReplaceAsync(string tempPath, string path, CancellationToken ct)
        {
            if (!_failed)
            {
                _failed = true;
                throw new IOException("the share did not answer");
            }
            return inner.ReplaceAsync(tempPath, path, ct);
        }
    }
}
