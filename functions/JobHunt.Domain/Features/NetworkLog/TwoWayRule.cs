namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>What one thread says about a conversation: the person in it and when both sides wrote.</summary>
/// <param name="From">The person's From header, off the first message they sent.</param>
/// <param name="Address">The person's address, lower case: the only address a draft goes to.</param>
/// <param name="FirstTwoWayAt">When the thread first had both sides in it (or the invite's time).</param>
/// <param name="LastInboundAt">The person's newest message.</param>
/// <param name="LastOutboundAt">Steve's newest message, null for an invite he has not answered.</param>
/// <param name="NewestMessageId">The newest message's Message-Id, for a reply's In-Reply-To.</param>
public sealed record TwoWayThread(string From, string Address, DateTimeOffset FirstTwoWayAt, DateTimeOffset LastInboundAt, DateTimeOffset? LastOutboundAt, string? NewestMessageId);

/// <summary>
/// A thread is a conversation when a person wrote in it and Steve wrote in it, or when a person sent an invite
/// (a message with a calendar part), which is a meeting both sides agreed to have. Steve alone is a cold send; a
/// person alone is mail Steve has not answered; a thread of machines is neither.
/// </summary>
public static class TwoWayRule
{
    /// <summary>The thread's conversation, or null when it is not two-way.</summary>
    public static TwoWayThread? Read(ThreadSummary thread)
    {
        var messages = thread.Messages.OrderBy(m => m.At).ToList();
        var human = messages.Where(m => !m.Outbound && HumanSenderRule.IsHuman(m.From)).ToList();
        var outbound = messages.Where(m => m.Outbound || HumanSenderRule.IsSteve(m.From)).ToList();
        if (human.Count == 0)
        {
            return null;
        }
        var first = human[0];
        string address = Mail.Address(first.From);
        var theirs = human.Where(m => Mail.Address(m.From) == address).ToList();
        string? newest = messages[^1].RfcMessageId;
        if (outbound.Count > 0)
        {
            var started = first.At > outbound[0].At ? first.At : outbound[0].At;
            return new TwoWayThread(first.From, address, started, theirs[^1].At, outbound[^1].At, newest);
        }
        var invite = theirs.FirstOrDefault(m => m.HasCalendarPart);
        return invite is null ? null : new TwoWayThread(first.From, address, invite.At, theirs[^1].At, null, newest);
    }
}
