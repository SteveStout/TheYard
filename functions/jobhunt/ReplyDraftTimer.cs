using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

// #region summary
/// <summary>
/// Reads the inbox every five minutes, from two past the hour so it never fires with the ATS sweep at :20 and :50, for a
/// message a person wrote on a job thread, one whose writer's address or work
/// domain a tracker row holds, and queues one dispatcher item for
/// each that writes a draft reply in Steve's voice to the share and one line on TODAY.md (ReplyDrafts). When POUNCE_RUN
/// says live it puts each written draft into its thread's Gmail drafts with the network log's draft writer, which has no
/// send. Nothing here sends: the draft waits for Steve's own "send".
/// </summary>
// #endregion summary
/// <remarks>
/// POUNCE_RUN picks the mode: "off" reads and writes nothing, "live" places the drafts in Gmail, anything else, unset
/// included, queues items whose draft stays on the share and in the DONE note. It needs the Gmail client settings the
/// ATS sweep uses; without them the pass is blocked and says so. No model runs here. Once jobhunt.ReplyDrafts exists on
/// the campaign's database (CAMPAIGN_SQL) every decision is also written as a row, and the run line gains rows,
/// mismatch and sql_error; until then the pass runs on its files alone, as it did.
/// </remarks>
public sealed class ReplyDraftTimer(IMergeStore store, IHttpClientFactory http, TimeProvider time, ILogger<ReplyDraftTimer> log)
{
    /// <summary>Whether the table is there, asked once a process and once an hour while it is not.</summary>
    private static readonly ReplyDraftSwitch Table = new(TimeSpan.FromHours(1));

    /// <summary>One firing: with the switch not off, one reply pass and its run line.</summary>
    [Function("ReplyDraft")]
    public async Task RunAsync([TimerTrigger("0 2-59/5 * * * *")] TimerInfo timer, CancellationToken ct)
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
            IReplyDraftWriter? drafts = mode == PounceHandoff.Live ? new ThreadDrafts(new GmailDrafts(http, google, time)) : null;
            var (rows, probeError) = await RowsAsync(ct);
            var pass = await new ReplyDrafts(store, new GmailInbox(http, google, time), drafts, time) { Rows = rows }.RunAsync(ct);
            run.Count("read", pass.Read).Count("waiting", pass.Waiting).Count("queued", pass.Queued).Count("already", pass.Already).Count("placed", pass.Placed).Count("skipped", pass.Skipped);
            if (rows is not null || probeError > 0)
            {
                run.Count("rows", pass.Rows).Count("mismatch", pass.Mismatch).Count("sql_error", pass.SqlError + probeError);
            }
            run.Came(pass.Queued + pass.Placed > 0 ? "success" : "unchanged");
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

    /// <summary>The rows beside the files when the table is there, and 1 when asking SQL failed; a failure never stops the pass.</summary>
    private async Task<(ReplyDraftLedger? Rows, int ProbeError)> RowsAsync(CancellationToken ct)
    {
        string? sql = Environment.GetEnvironmentVariable("CAMPAIGN_SQL");
        if (string.IsNullOrWhiteSpace(sql))
        {
            return (null, 0);
        }
        var table = new SqlReplyDraftRows(sql);
        try
        {
            return (await Table.PresentAsync(table.ReadyAsync, time.GetUtcNow(), ct) ? new ReplyDraftLedger(table) : null, 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "reply drafts: the rows' table could not be asked for; the pass runs on its files");
            return (null, 1);
        }
    }

    /// <summary>The network log's draft writer as the reply pass asks it: one plain draft into the thread, nothing sent.</summary>
    private sealed class ThreadDrafts(IDraftWriter drafts) : IReplyDraftWriter
    {
        /// <inheritdoc />
        public async Task<string> CreateDraftAsync(ThreadReply reply, string text, CancellationToken ct) =>
            (await drafts.CreateDraftAsync(new DraftMail(reply.Thread, reply.To, reply.Subject, text, System.Net.WebUtility.HtmlEncode(text).Replace("\n", "<br>", StringComparison.Ordinal), Header(reply.InReplyTo), Header(reply.References)), ct)).DraftId;

        private static string? Header(string value) => value.Length == 0 ? null : value;
    }
}
