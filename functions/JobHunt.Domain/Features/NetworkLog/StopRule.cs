namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>Why a conversation stops being drafted for good, read from its thread.</summary>
/// <param name="State">Stopped or Closed.</param>
/// <param name="Reason">The stop phrase, or "bounce".</param>
public sealed record StopVerdict(ConversationState State, string Reason);

/// <summary>
/// A person who asked to stop is never followed up: the campaign's stop phrases (StopPhrases) in any of their
/// messages after the conversation began make the row Stopped. The mail system answering for a message on the
/// thread closes it as a bounce. A phrase quoted inside a reply still stops it, which is the safe direction.
/// </summary>
public static class StopRule
{
    /// <summary>The verdict for the thread, or null when nothing in it stops the row.</summary>
    public static StopVerdict? Read(ThreadSummary thread, DateTimeOffset firstTwoWayAt)
    {
        foreach (var message in thread.Messages.Where(m => m.At >= firstTwoWayAt && !m.Outbound).OrderBy(m => m.At))
        {
            if (HumanSenderRule.IsBounce(message.From))
            {
                return new StopVerdict(ConversationState.Closed, "bounce");
            }
            string text = (message.Snippet ?? string.Empty).ToLowerInvariant().Replace('\u2019', '\'');
            if (HumanSenderRule.IsHuman(message.From) && StopPhrases.All.FirstOrDefault(phrase => text.Contains(phrase, StringComparison.Ordinal)) is { } phrase)
            {
                return new StopVerdict(ConversationState.Stopped, phrase);
            }
        }
        return null;
    }
}
