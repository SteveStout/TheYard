namespace TheYard.Application;

// The one email this site sends: a password reset link (ADR: Accounts and
// per-user bids, addendum). A port, so the API can be held without a
// mailbox and a container with no sender configured says so.

// #region email-port
/// <summary>Where a message goes. Configured means a sender exists; Reason says why not when it does not.</summary>
public interface IEmailSender
{
    bool Configured { get; }

    string Reason { get; }

    /// <summary>Send one plain-text message; true when the sender accepted it, false when it refused or failed. Never throws.</summary>
    Task<bool> SendAsync(string to, string subject, string text, CancellationToken cancellation);
}

/// <summary>The port wired to nothing: no sender, and a sentence for the card.</summary>
public sealed class NullEmailSender(string reason) : IEmailSender
{
    public static readonly NullEmailSender Instance = new("no email sender is configured on this site; the operator can mint a reset link from the Admin tab");

    public bool Configured => false;

    public string Reason => reason;

    public Task<bool> SendAsync(string to, string subject, string text, CancellationToken cancellation) => Task.FromResult(false);
}
// #endregion email-port
