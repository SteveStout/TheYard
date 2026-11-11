using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

// #region summary
/// <summary>
/// Reads the inbox every five minutes for a message a person wrote on a job thread, one whose writer's address or work
/// domain a tracker row holds, and queues one dispatcher item for
/// each that writes a draft reply in Steve's voice to the share and one line on TODAY.md (ReplyDrafts). When POUNCE_RUN
/// says live it puts each written draft into its thread's Gmail drafts with the network log's draft writer, which has no
/// send. Nothing here sends: the draft waits for Steve's own "send".
/// </summary>
// #endregion summary
/// <remarks>
/// POUNCE_RUN picks the mode: "off" reads and writes nothing, "live" places the drafts in Gmail, anything else, unset
/// included, queues items whose draft stays on the share and in the DONE note. It needs the Gmail client settings the
/// ATS sweep uses; without them the pass is blocked and says so. No model runs here.
/// </remarks>
public sealed class ReplyDraftTimer(IMergeStore store, IHttpClientFactory http, TimeProvider time, ILogger<ReplyDraftTimer> log)
{
    /// <summary>One firing: with the switch not off, one reply pass and its run line.</summary>
    [Function("ReplyDraft")]
    public async Task RunAsync([TimerTrigger("0 */5 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var run = new RunLine("ReplyDraft", time.GetUtcNow());
        try
        {
            string mode = PounceHandoff.Mode(Environment.GetEnvironmentVariable("POUNCE_RUN"));
            if (mode == PounceHandoff.Off)
            {
                run.Came("unchanged", "switch_off");
                return;
            }
            if (GoogleCredentials.FromEnvironment() is not { } google)
            {
                run.Came("blocked", "no_gmail");
                return;
            }
            IReplyDraftWriter? drafts = mode == PounceHandoff.Live ? new ThreadDrafts(new GmailDrafts(http, google)) : null;
            var pass = await new ReplyDrafts(store, new GmailInbox(http, google), drafts, time).RunAsync(ct);
            run.Count("read", pass.Read).Count("waiting", pass.Waiting).Count("queued", pass.Queued).Count("already", pass.Already).Count("placed", pass.Placed).Count("skipped", pass.Skipped)
                .Came(pass.Queued + pass.Placed > 0 ? "success" : "unchanged");
        }
        catch (Exception ex)
        {
            run.Failed(ex);
            throw;
        }
        finally
        {
            RunLines.Write(log, run, time.GetUtcNow());
        }
    }

    /// <summary>The network log's draft writer as the reply pass asks it: one plain draft into the thread, nothing sent.</summary>
    private sealed class ThreadDrafts(IDraftWriter drafts) : IReplyDraftWriter
    {
        /// <inheritdoc />
        public async Task<string> CreateDraftAsync(string threadId, string to, string subject, string text, CancellationToken ct) =>
            (await drafts.CreateDraftAsync(new DraftMail(threadId, to, subject, text, System.Net.WebUtility.HtmlEncode(text).Replace("\n", "<br>", StringComparison.Ordinal), null, null), ct)).DraftId;
    }
}
