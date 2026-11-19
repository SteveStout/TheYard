using JobHunt.Application.Shared.Mail;
using JobHunt.Application.Shared.Store;
using JobHunt.Domain.Shared.Clock;
using JobHunt.Domain.Shared.Mail;
using JobHunt.Domain.Shared.Tracker;

namespace JobHunt.Infrastructure.Features.Campaign;

/// <summary>
/// The one gate on the Function's mail sender for no-email days (selfheal1120 version 2): on a Central day
/// <see cref="NoEmailDays"/> lists, it lets through only mail whose every recipient is Steve's own address (the 07:30
/// card, a NOT WORKING mail) and throws <see cref="MailHeldException"/> for anything else, before the inner sender is
/// called. The caller writes no_email_day on its run line, counts nothing against a cap, and tries again the next
/// open day. It wraps the sender and changes no rule of a caller: every GmailSender the timers build is wrapped.
/// </summary>
/// <param name="inner">The sender a passed mail goes to.</param>
/// <param name="days">The listed days.</param>
/// <param name="time">The clock the Central day is read from.</param>
/// <param name="own">Steve's own address.</param>
public sealed class NoEmailDayGate(IMailSender inner, NoEmailDays days, TimeProvider time, string own) : IMailSender
{
    /// <summary>The word a held send writes on its run line.</summary>
    public const string Reason = "no_email_day";

    /// <summary>Whether today, in Central, is listed.</summary>
    public bool HeldToday => days.Listed(DateOnly.FromDateTime(CentralClock.Local(time.GetUtcNow())));

    /// <inheritdoc />
    public Task<SentMail> SendAsync(OutgoingMail mail, CancellationToken ct) =>
        HeldToday && !OwnOnly(mail.To, own) ? throw new MailHeldException(Reason) : inner.SendAsync(mail, ct);

    /// <summary>Whether every address in a To line is the given one (case and spaces aside); an empty line is not.</summary>
    public static bool OwnOnly(string to, string own)
    {
        string[] each = (to ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return each.Length > 0 && each.All(a => string.Equals(Bare(a), own, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The share's file as its days; a missing or unreadable file lists nothing (W16 reads <see cref="NoEmailDays.Read"/>).</summary>
    public static async Task<NoEmailDays> ReadAsync(IShareReader share, CancellationToken ct)
    {
        try
        {
            return await share.ExistsAsync(NoEmailDays.File, ct) ? NoEmailDays.Parse(TrackerCsvText.Decode(await share.ReadAsync(NoEmailDays.File, ct))) : NoEmailDays.None;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return NoEmailDays.None;
        }
    }

    private static string Bare(string address) =>
        address.LastIndexOf('<') is var open and >= 0 && address.IndexOf('>', open) is var close and > 0 ? address[(open + 1)..close].Trim() : address.Trim();
}
