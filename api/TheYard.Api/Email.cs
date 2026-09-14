using System.Collections.Concurrent;
using Azure;
using Azure.Communication.Email;
using Azure.Core;
using TheYard.Application;

namespace TheYard.Api;

// #region acs-sender
/// <summary>
/// Azure Communication Services Email, as the containers' own identity (ADR:
/// Accounts and per-user bids, addendum). Two plain settings, the resource's
/// endpoint and the sender address on its Azure-managed domain; no key,
/// because the identity that reads both stores is allowed to send here too,
/// and the credential is the store's own, built by the same rule from the
/// same settings (CosmosStore.CredentialFor) rather than a second one here.
/// A send is fire-and-forget on the service side (WaitUntil.Started): the
/// service queues it and delivery is its problem, not a request thread's.
/// </summary>
public sealed class AcsEmailSender(string endpoint, string from, TokenCredential credential, ILogger<AcsEmailSender> logger) : IEmailSender
{
    private readonly EmailClient _client = new(new Uri(endpoint), credential);

    public bool Configured => true;

    public string Reason => $"sent as {from}";

    public async Task<bool> SendAsync(string to, string subject, string text, CancellationToken cancellation)
    {
        try
        {
            var operation = await _client.SendAsync(WaitUntil.Started, from, to, subject, text, cancellationToken: cancellation);
            return operation is not null;
        }
        catch (Exception ex)
        {
            // The type, never the message: a service error can quote the address.
            logger.LogWarning("An email was not sent: {Type}", ex.GetType().Name);
            return false;
        }
    }

    /// <summary>
    /// The sender the configuration describes, or none: both settings, neither
    /// a placeholder. The credential is asked for only when there is a sender
    /// to give it to, so a test host and a developer's machine build none.
    /// </summary>
    public static IEmailSender FromConfiguration(string? endpoint, string? from, Func<TokenCredential> credential, ILogger<AcsEmailSender> logger)
    {
        static bool Usable(string? value) => !string.IsNullOrWhiteSpace(value) && !value.StartsWith("__", StringComparison.Ordinal);
        return Usable(endpoint) && Usable(from) && Uri.TryCreate(endpoint, UriKind.Absolute, out _)
            ? new AcsEmailSender(endpoint!.Trim(), from!.Trim(), credential(), logger)
            : NullEmailSender.Instance;
    }
}
// #endregion acs-sender

// #region forgot-limit
/// <summary>
/// One reset email per address per five minutes, whoever asks. The endpoint
/// is public and answers the same sentence whether the address is known or
/// not, so without this a stranger could have the site mail one address all
/// night. This process's memory, like the registration allowance: two
/// containers means two allowances, which is still a ceiling.
/// </summary>
public sealed class ForgotLimit(TimeSpan spacing, Func<DateTimeOffset> clock)
{
    public static readonly TimeSpan DefaultSpacing = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _last = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True, and the slot is taken, when this address has not been mailed in the last few minutes.</summary>
    public bool TryTake(string email)
    {
        var now = clock();
        bool taken = false;
        _last.AddOrUpdate(
            email.Trim(),
            _ => { taken = true; return now; },
            (_, last) =>
            {
                if (now - last >= spacing)
                {
                    taken = true;
                    return now;
                }
                return last;
            });
        return taken;
    }
}
// #endregion forgot-limit
