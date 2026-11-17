namespace JobHunt.Application.Features.NetworkLog;

/// <summary>What one Monday's build did.</summary>
/// <param name="Due">Rows owed a follow-up, after the cap.</param>
/// <param name="Drafted">Gmail drafts written and read back, or in shadow would-drafts that passed their checks.</param>
/// <param name="RenderFailed">Rows whose render failed a check; each stays Open and is tried next Monday.</param>
/// <param name="Failed">The checks that failed, by row.</param>
/// <param name="Posts">Drafts, or would-drafts, that name a post.</param>
public sealed record MondayResult(int Due, int Drafted, int RenderFailed, IReadOnlyList<string> Failed, int Posts = 0)
{
    /// <summary>Email reconnects sent and read back from Sent (NETWORK_SEND on); they count in Drafted too.</summary>
    public int Sent { get; init; }

    /// <summary>Email reconnects a guard held, by the guard.</summary>
    public IReadOnlyDictionary<string, int> Held { get; init; } = new Dictionary<string, int>();
}

/// <summary>
/// The week's follow-ups, on a Monday: the due rows through FOLLOWUP-v1 and its checks, written to the share as
/// would-drafts in shadow. In `on`, a row whose newest exchange was by email is a reconnect in RECONNECT-v1 (Reconnects):
/// its guards, its code, and, while NETWORK_SEND reads shadow, a Gmail draft into its own thread under
/// Follow-up/Ready read back before the row is marked Drafted, beside the HTML that would send; with NETWORK_SEND on,
/// the send itself. A row is Drafted only once its DraftId is stored, so a second build the same Monday writes
/// nothing twice, except that a build after NETWORK_SEND flips to on sends the rows the day's shadow build drafted,
/// deleting each draft first. A row whose newest exchange was on LinkedIn goes to the week's LinkedIn queue under
/// section A in FOLLOWUP-v1, its text beside it, for Steve's send; nothing types it into a thread without that. A
/// LinkedIn row holding a post and its code names the post when the blog's feed shows it went out after Steve's last
/// message; a draft whose post line fails a check is written without it.
/// </summary>
public sealed class MondayDrafts(IConversationStore rows, IThreadReader gmail, IDraftWriter drafts, IMergeStore share, IBlogPosts blog)
{
    /// <summary>The parent label.</summary>
    public const string Parent = "Follow-up";

    /// <summary>The label every draft carries.</summary>
    public const string Ready = "Follow-up/Ready";

    /// <summary>The email reconnects' sender and its mode; the default drafts in shadow.</summary>
    public Reconnects? Reconnect { get; init; }

    /// <summary>Builds the Monday's follow-ups from the open rows.</summary>
    public async Task<MondayResult> RunAsync(NetworkMode mode, DateOnly monday, IReadOnlyList<Conversation> open, DateTimeOffset now, CancellationToken ct)
    {
        var batch = DueRule.Batch(open, now);
        var posts = batch.Any(r => r.BlogSlug is not null && r.RefCode is not null) ? await blog.PostsAsync(ct) : [];
        var rendered = batch.Select(row => Render(row, posts)).ToList();
        int named = rendered.Count(r => r.Item3.Count == 0 && r.Item2.Text.Contains(BlogLine.Site, StringComparison.Ordinal));
        var failed = rendered.Where(r => r.Item3.Count > 0).Select(r => $"{r.row.PersonKey}: {string.Join(", ", r.Item3)}").ToList();
        if (mode != NetworkMode.On)
        {
            await share.EnsureFolderAsync(NetworkFiles.WouldDrafts, ct);
            await share.WriteAsync(NetworkFiles.WouldDraftsPath(monday), TrackerCsvText.Encode(NetworkFiles.WouldDraftsText(monday, rendered)), ct);
            return new MondayResult(batch.Count, rendered.Count(r => r.Item3.Count == 0), failed.Count, failed, named);
        }
        var reconnects = Reconnect ?? new Reconnects(rows, gmail, drafts, share, TimeProvider.System);
        var email = batch.Where(r => r.LastChannel == Channel.Email && r.Email is not null && r.ThreadIds.Count > 0).ToList();
        if (reconnects.Mode == SendMode.On)
        {
            // A flip after the day's shadow build: its drafts are sent instead, each draft deleted first so Steve has no second copy.
            foreach (var row in open.Where(r => r.State == ConversationState.Drafted && r.DraftChannel == Channel.Email && r.Email is not null && r.ThreadIds.Count > 0
                && r.DraftId is { } id && !id.StartsWith(Reconnects.SentPrefix, StringComparison.Ordinal) && r.DraftedAt is { } at && DateOnly.FromDateTime(CentralClock.Local(at)) == monday))
            {
                await drafts.DeleteDraftAsync(row.DraftId!, ct);
                email.Add(row with { State = ConversationState.Open, DraftId = null, DraftedAt = null });
            }
        }
        var sent = await reconnects.RunAsync(monday, email, open, now, ct);
        var linkedIn = rendered.Where(r => r.row.LastChannel == Channel.LinkedIn && r.row.ThreadIds.Count > 0).ToList();
        var failedOn = linkedIn.Where(r => r.Item3.Count > 0).Select(r => $"{r.row.PersonKey}: {string.Join(", ", r.Item3)}").Concat(sent.Failed).ToList();
        int written = sent.Sent + sent.Drafted + await LinkedInAsync(monday, [.. linkedIn.Where(r => r.Item3.Count == 0).Select(r => (r.row, r.Item2))], ct);
        int namedOn = linkedIn.Count(r => r.Item3.Count == 0 && r.Item2.Text.Contains(BlogLine.Site, StringComparison.Ordinal));
        return new MondayResult(batch.Count, written, failedOn.Count, failedOn, namedOn) { Sent = sent.Sent, Held = sent.Held };
    }

    /// <summary>
    /// The LinkedIn rows' follow-ups as the week's queue under section A, each draft's text beside it, for Steve's send.
    /// The rows stay Open: his send moves their last exchange on the next LinkedIn read, and one he leaves is due again
    /// the next Monday until it goes stale.
    /// </summary>
    private async Task<int> LinkedInAsync(DateOnly monday, IReadOnlyList<(Conversation Row, FollowUpDraft Draft)> due, CancellationToken ct)
    {
        if (due.Count == 0)
        {
            return 0;
        }
        await share.EnsureFolderAsync("networklog/texts", ct);
        await share.EnsureFolderAsync(FollowUpQueue.Texts(monday), ct);
        foreach (var (row, draft) in due)
        {
            await share.WriteAsync(NetworkFiles.LinkedInDraftPath(monday, row.ThreadIds[0]), TrackerCsvText.Encode(draft.Text), ct);
        }
        await share.WriteAsync(FollowUpQueue.Path(monday), TrackerCsvText.Encode(NetworkFiles.LinkedInQueueText(monday, due.Select(d => d.Row))), ct);
        return due.Count;
    }

    private static (Conversation row, FollowUpDraft draft, IReadOnlyList<string> checks) Render(Conversation row, IReadOnlyList<BlogPost> posts)
    {
        var plain = DraftTemplate.Render(row.FirstName, row.TopicLine ?? "", row.LastSubject ?? "");
        if (BlogLine.For(row, posts) is { } post && DraftTemplate.Render(row.FirstName, row.TopicLine ?? "", row.LastSubject ?? "", post) is var named
            && Checks(row, named).Count == 0)
        {
            return (row, named, []);
        }
        return (row, plain, Checks(row, plain));
    }

    /// <summary>The template's checks, less the subject's for a LinkedIn row: a LinkedIn message has no subject line.</summary>
    private static IReadOnlyList<string> Checks(Conversation row, FollowUpDraft draft) =>
        row.LastChannel == Channel.LinkedIn ? [.. DraftTemplate.Check(draft).Where(c => c != "subject")] : DraftTemplate.Check(draft);
}
