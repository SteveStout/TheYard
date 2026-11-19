namespace JobHunt.Application.Shared.Mail;

/// <summary>One email for the mail port to send.</summary>
public sealed record OutgoingMail(string To, string Subject, string Text, string Html)
{
    /// <summary>The Gmail thread a reply goes into, null for a new thread (the campaign's first email).</summary>
    public string? ThreadId { get; init; }

    /// <summary>The Message-Id the reply answers, for In-Reply-To and References, null for none.</summary>
    public string? InReplyTo { get; init; }
}

/// <summary>What Gmail answered for a sent email: its ids, and the Message-Id a delivery failure quotes back.</summary>
public sealed record SentMail(string MessageId, string ThreadId, string? RfcMessageId);

/// <summary>Sends one email from Steve's Gmail. Only the campaign's `on` path reaches it; a test holds shadow away from it.</summary>
public interface IMailSender
{
    /// <summary>Sends the message and answers its ids.</summary>
    Task<SentMail> SendAsync(OutgoingMail mail, CancellationToken ct);
}

/// <summary>A send the no-email-day gate held: nothing was sent, and the caller tries again the next open day.</summary>
/// <param name="reason">The run line's word.</param>
public sealed class MailHeldException(string reason) : Exception($"mail held: {reason}")
{
    /// <summary>The run line's word.</summary>
    public string Reason { get; } = reason;
}

/// <summary>Reads a sent message back: a send counts only once Gmail shows it under Sent.</summary>
public interface ISentReader
{
    /// <summary>Whether the message carries Gmail's SENT label.</summary>
    Task<bool> InSentAsync(string messageId, CancellationToken ct);
}

/// <summary>
/// Where an address comes from when no public source published one. No third-party finder is used, so the one
/// adapter is NoFinder, which answers nothing. The port exists anyway, and that is the decision worth reading:
/// a finder is a question to settle on the shadow week's own numbers rather than on a guess, and keeping the
/// seam means settling it costs one adapter instead of a change to every caller.
/// </summary>
public interface IAddressFinder
{
    /// <summary>The finder's name for the card and the pass log.</summary>
    string Name { get; }

    /// <summary>An address with its source, or null with nothing spent.</summary>
    Task<FoundAddress?> FindAsync(string fullName, string domain, CancellationToken ct);
}

/// <summary>An address and the URL it was read from.</summary>
public sealed record FoundAddress(string Address, string SourceUrl);
