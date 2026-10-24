namespace JobHunt.Domain.Features.Ats;

/// <summary>One message as the sweep reads it.</summary>
/// <param name="Id">The Gmail message id.</param>
/// <param name="ThreadId">The Gmail thread id.</param>
/// <param name="From">The From header as written.</param>
/// <param name="Subject">The Subject header.</param>
/// <param name="At">When Gmail received it.</param>
/// <param name="Text">The body as plain text, markup removed.</param>
public sealed record InboxMessage(string Id, string ThreadId, string From, string Subject, DateTimeOffset At, string Text)
{
    /// <summary>
    /// The body with its line breaks kept, for a reader that goes line by line (a job alert's blocks);
    /// Text joins every line into one, which is what the ATS rules' phrases need.
    /// </summary>
    public string Lines { get; init; } = "";

    /// <summary>The HTML parts as sent, for a reader that needs a link's address (a SuccessFactors agent has no plain part).</summary>
    public string Html { get; init; } = "";

    /// <summary>The address in the From header, lower case.</summary>
    public string Address => Mail.Address(From);

    /// <summary>The display name in the From header, or empty.</summary>
    public string Name => Mail.Name(From);
}
