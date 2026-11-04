namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>What a Drafted row becomes after a pass reads its thread.</summary>
public enum DraftedOutcome
{
    /// <summary>The draft is still waiting, or was sent less than 21 days ago with no answer.</summary>
    StaysDrafted,

    /// <summary>The draft was sent and the person answered: Open again, one more follow-up counted.</summary>
    Answered,

    /// <summary>The draft was sent and met 21 days of silence: Quiet, never redrafted.</summary>
    Quiet,

    /// <summary>The draft is gone and nothing was sent: Steve chose not to, so the row closes as declined.</summary>
    Declined,
}

/// <summary>
/// Silence is the answer: a follow-up Steve sent that met 21 days of no reply closes the conversation as Quiet,
/// and it is never drafted again. A reply opens it again; a draft deleted unsent closes it as declined.
/// </summary>
public static class QuietRule
{
    /// <summary>The days of silence after a sent follow-up that make a row Quiet.</summary>
    public const int Days = 21;

    /// <summary>The row's next state from the draft's presence and the thread's messages after DraftedAt.</summary>
    /// <param name="row">A Drafted row.</param>
    /// <param name="draftExists">Whether the draft is still in Drafts.</param>
    /// <param name="thread">The thread the draft was written into.</param>
    /// <param name="now">The pass's clock.</param>
    public static DraftedOutcome Next(Conversation row, bool draftExists, ThreadSummary thread, DateTimeOffset now)
    {
        if (row.State != ConversationState.Drafted || row.DraftedAt is not { } drafted || draftExists)
        {
            return DraftedOutcome.StaysDrafted;
        }
        var sent = thread.Messages.Where(m => m.At >= drafted && (m.Outbound || HumanSenderRule.IsSteve(m.From))).OrderBy(m => m.At).FirstOrDefault();
        if (sent is null)
        {
            return DraftedOutcome.Declined;
        }
        bool answered = thread.Messages.Any(m => m.At > sent.At && !m.Outbound && HumanSenderRule.IsHuman(m.From));
        if (answered)
        {
            return DraftedOutcome.Answered;
        }
        return now >= sent.At.AddDays(Days) ? DraftedOutcome.Quiet : DraftedOutcome.StaysDrafted;
    }
}
