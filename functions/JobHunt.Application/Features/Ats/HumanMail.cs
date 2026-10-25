using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Application.Features.Ats;

/// <summary>
/// Human mail never waits for 4 AM. The inbox pass asks this whether a
/// person, not a relay or a machine, has written in the last two days to a thread that holds no reply draft;
/// when one has, the pass fires the reply run (the existing nightly mode, Opus, drafts only) at most once every
/// four hours, between 7 AM and 9 PM Central. Nothing here sends, drafts or archives: it reads, and it keeps the
/// time it last fired in inbox-pass/reply-fired.txt on the share so the four hours hold across passes.
/// </summary>
public sealed partial class HumanMail(IMergeStore store, IInbox inbox, TimeProvider time)
{
    /// <summary>The share file that holds the last time the reply run was fired for human mail, ISO 8601 UTC.</summary>
    public const string Fired = "inbox-pass/reply-fired.txt";

    /// <summary>The fewest hours between two fires.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromHours(4);

    /// <summary>The first and the last Central hour a fire may start in: 7 AM up to 9 PM.</summary>
    public const int FirstHour = 7, EndHour = 21;

    /// <summary>The mail this reads: the inbox, the last two days, nothing Steve sent.</summary>
    public static MailSearch Search { get; } = new([], [], InboxOnly: true, NewerThanDays: 2, NotFromMe: true);

    /// <summary>Steve's own address, never a person waiting on him.</summary>
    public const string Steve = "stevenstout11@gmail.com";

    /// <summary>
    /// Whether to fire now, with the threads waiting and why: outside the hours, inside the four hours since the
    /// last fire, nobody waiting, or the waiting threads named by sender.
    /// </summary>
    public async Task<(bool Fire, int Waiting, string Why)> CheckAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var local = CentralClock.Local(now);
        if (local.Hour < FirstHour || local.Hour >= EndHour)
        {
            return (false, 0, $"outside {FirstHour} AM to {EndHour - 12} PM Central");
        }
        if (await LastFiredAsync(ct) is { } last && now - last < Every)
        {
            return (false, 0, $"fired {last:HH:mm}Z, the next not before {last + Every:HH:mm}Z");
        }
        var waiting = new List<string>();
        var threads = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in await inbox.FindAsync(Search, 50, ct))
        {
            var message = await inbox.ReadAsync(id, ct);
            if (!IsPerson(message.Address) || !threads.Add(message.ThreadId))
            {
                continue;
            }
            if (await inbox.ThreadHasDraftAsync(message.ThreadId, ct))
            {
                continue;
            }
            // A thread whose newest message is Steve's is answered: the person's mail stays in the inbox for two days
            // after his reply, and without this the run would fire on it every four hours until it aged out.
            if (await inbox.ThreadNewestSenderAsync(message.ThreadId, ct) is { } newest && Mail.Address(newest) == Steve)
            {
                continue;
            }
            waiting.Add(message.Address);
        }
        return waiting.Count == 0
            ? (false, 0, "no person waiting")
            : (true, waiting.Count, "waiting: " + string.Join(", ", waiting.Distinct(StringComparer.Ordinal)));
    }

    /// <summary>Keeps the time of a fire, so the next four hours hold.</summary>
    public async Task MarkFiredAsync(CancellationToken ct)
    {
        await store.EnsureFolderAsync("inbox-pass", ct);
        await store.WriteAsync(Fired, TrackerCsvText.Encode(time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)), ct);
    }

    /// <summary>
    /// True when the address is a person: not Steve, not a machine (Mail.IsMachine), and not a relay that speaks
    /// for one (a LinkedIn InMail notice, a digest, an alert, a newsletter), whose real sender is not in the mail.
    /// </summary>
    public static bool IsPerson(string address) =>
        address.Length > 0 && address != Steve && !Mail.IsMachine(address) && !Relay().IsMatch(address[..Math.Max(0, address.IndexOf('@'))]);

    private async Task<DateTimeOffset?> LastFiredAsync(CancellationToken ct) =>
        await store.ExistsAsync(Fired, ct)
        && DateTimeOffset.TryParse(TrackerCsvText.Decode(await store.ReadAsync(Fired, ct)).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
            ? at
            : null;

    [GeneratedRegex(@"no[-._]?reply|notification|alert|digest|mailer|update|newsletter|news|marketing|^info$|^hello$|^support$|^team$|inmail|messaging|invitations?", RegexOptions.IgnoreCase)]
    private static partial Regex Relay();
}
