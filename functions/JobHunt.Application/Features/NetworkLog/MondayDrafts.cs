namespace JobHunt.Application.Features.NetworkLog;

/// <summary>What one Monday's build did.</summary>
/// <param name="Due">Rows owed a follow-up, after the cap.</param>
/// <param name="Drafted">Gmail drafts written and read back, or in shadow would-drafts that passed their checks.</param>
/// <param name="RenderFailed">Rows whose render failed a check; each stays Open and is tried next Monday.</param>
/// <param name="Failed">The checks that failed, by row.</param>
/// <param name="Posts">Drafts, or would-drafts, that name a post.</param>
public sealed record MondayResult(int Due, int Drafted, int RenderFailed, IReadOnlyList<string> Failed, int Posts = 0);

/// <summary>
/// The week's follow-ups, on a Monday: the due rows through FOLLOWUP-v1 and its checks, written to the share as
/// would-drafts in shadow, and in `on` written as Gmail drafts into each row's own thread under Follow-up/Ready,
/// to the address the person wrote from, each read back before the row is marked Drafted. Nothing sends: the draft
/// port has no send, and a row is Drafted only once its DraftId is stored, so a second build the same Monday drafts
/// nothing twice. A row whose newest exchange was on LinkedIn waits for the LinkedIn queue. A row holding a post and
/// its code names the post when the blog's feed shows it went out after Steve's last message; a draft whose post line
/// fails a check is written without it.
/// </summary>
public sealed class MondayDrafts(IConversationStore rows, IThreadReader gmail, IDraftWriter drafts, IMergeStore share, IBlogPosts blog)
{
    /// <summary>The parent label.</summary>
    public const string Parent = "Follow-up";

    /// <summary>The label every draft carries.</summary>
    public const string Ready = "Follow-up/Ready";

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
        int written = 0;
        string? label = null;
        foreach (var (row, draft, checks) in rendered.Where(r => r.Item3.Count == 0 && r.row.LastChannel == Channel.Email && r.row.Email is not null && r.row.ThreadIds.Count > 0))
        {
            if (label is null)
            {
                await drafts.EnsureLabelAsync(Parent, ct);
                label = await drafts.EnsureLabelAsync(Ready, ct);
            }
            var thread = await gmail.ReadSummaryAsync(row.ThreadIds[0], ct);
            string? newest = thread.Messages.LastOrDefault(m => m.RfcMessageId is not null)?.RfcMessageId;
            var (draftId, messageId) = await drafts.CreateDraftAsync(new DraftMail(row.ThreadIds[0], row.Email!, draft.Subject, draft.Text, draft.Html, newest, newest), ct);
            await drafts.LabelAsync(messageId, label, ct);
            if (!await drafts.DraftExistsAsync(draftId, ct))
            {
                failed.Add($"{row.PersonKey}: draft not read back");
                continue;
            }
            await rows.SetDraftAsync(row.Id, draftId, Channel.Email, now, ct);
            written++;
        }
        return new MondayResult(batch.Count, written, failed.Count, failed, named);
    }

    private static (Conversation row, FollowUpDraft draft, IReadOnlyList<string> checks) Render(Conversation row, IReadOnlyList<BlogPost> posts)
    {
        var plain = DraftTemplate.Render(row.FirstName, row.TopicLine ?? "", row.LastSubject ?? "");
        if (BlogLine.For(row, posts) is { } post && DraftTemplate.Render(row.FirstName, row.TopicLine ?? "", row.LastSubject ?? "", post) is var named
            && DraftTemplate.Check(named).Count == 0)
        {
            return (row, named, []);
        }
        return (row, plain, DraftTemplate.Check(plain));
    }
}
