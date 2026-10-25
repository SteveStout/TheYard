namespace JobHunt.Application.Features.Campaign;

/// <summary>
/// The campaign's part of the inbox pass (ADR-118, Decision 4; the review, F11). Delivery failures: Gmail puts
/// each in the thread of the email that failed, so a failure in a sent thread for the address the row sent to
/// marks the email bounced and the lead's address unverified, and two in one Central day write the trip row
/// the send reads first. Replies: once an hour, each real send of the last fourteen days with no reply yet is
/// read for a sender who is neither Steve nor the mail system; the first marks the email answered, and a reply
/// whose own lines ask to stop marks the company do-not-email and lists the thread in campaign/stop-threads.log,
/// which the reply run reads before it drafts. In shadow nothing was sent, so there is nothing to read.
/// </summary>
public sealed class CampaignInbox(IInbox inbox, IEmailLog log, ILeadStore leads, ICompanyBook companies, IMergeStore store, TimeProvider time)
{
    /// <summary>The threads a reply run must not draft in, one row each.</summary>
    public const string StopThreads = "campaign/stop-threads.log";

    /// <summary>How far back a send is still read for a reply.</summary>
    public const int ReplyDays = 14;

    /// <summary>Gmail's delivery failures of the last two days.</summary>
    public static MailSearch Bounces { get; } = new([DeliveryFailure.Sender], [DeliveryFailure.SubjectPhrase], NewerThanDays: DeliveryFailure.WindowDays);

    /// <summary>One of a sent thread's other senders' mail, inside the window a reply is read in.</summary>
    /// <param name="address">The sender.</param>
    public static MailSearch RepliesFrom(string address) => new([address], [], NewerThanDays: ReplyDays + 1);

    /// <summary>Runs the part and answers its line for the pass log: counts only.</summary>
    public async Task<string> RunAsync(bool readReplies, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(CentralClock.Local(now));
        var threads = await log.SentThreadsAsync(today.AddDays(-30), ct);
        if (threads.Count == 0)
        {
            return "0 sent threads, nothing to read";
        }

        int failures = 0, bounced = 0, bouncesToday = 0;
        foreach (string id in await inbox.FindAsync(Bounces, 20, ct))
        {
            var message = await inbox.ReadAsync(id, ct);
            if (DeliveryFailure.Read(message) is not { } bounce)
            {
                continue;
            }
            failures++;
            var sent = threads.FirstOrDefault(t => t.ThreadId == message.ThreadId && string.Equals(t.To, bounce.Recipient, StringComparison.OrdinalIgnoreCase));
            if (sent is null)
            {
                continue;
            }
            if (DateOnly.FromDateTime(CentralClock.Local(message.At)) == today)
            {
                bouncesToday++;
            }
            if (!sent.Bounced)
            {
                await log.MarkBouncedAsync(sent.EmailId, bounce.Reason, ct);
                await leads.MarkUnverifiedAsync(sent.LeadId, "bounced: " + bounce.Reason, ct);
                bounced++;
            }
        }
        string trip = "";
        if (bouncesToday >= BounceTrip.Bounces)
        {
            string switchLog = await store.ExistsAsync(BounceTrip.SwitchLog, ct) ? TrackerCsvText.Decode(await store.ReadAsync(BounceTrip.SwitchLog, ct)) : "";
            if (!BounceTrip.Tripped(switchLog))
            {
                await store.EnsureFolderAsync("campaign", ct);
                await store.AppendAsync(BounceTrip.SwitchLog, TrackerCsvText.Encode(BounceTrip.Row(today, bouncesToday) + "\r\n"), ct);
            }
            trip = $" | BLOCKED: {bouncesToday} bounces today, the campaign is held in shadow until a person clears {BounceTrip.SwitchLog}";
        }

        int replies = 0, stops = 0;
        if (readReplies)
        {
            foreach (var sent in threads.Where(t => !t.Bounced && t.ReplyReceivedAt is null && today.DayNumber - t.CentralDay.DayNumber <= ReplyDays))
            {
                var senders = await inbox.ThreadSendersAsync(sent.ThreadId, ct);
                string self = senders.Count > 0 ? Mail.Address(senders[0]) : "";
                var others = senders.Skip(1).Select(Mail.Address).Where(a => a != self && !a.StartsWith("mailer-daemon@", StringComparison.Ordinal)).Distinct().ToList();
                if (others.Count == 0)
                {
                    continue;
                }
                await log.MarkReplyAsync(sent.ThreadId, now, ct);
                replies++;
                if (await StopAsync(sent, others, ct) is { } phrase)
                {
                    stops++;
                    if (await companies.FindAsync(sent.Key, ct) is { } company)
                    {
                        await companies.MarkDoNotEmailAsync(company.Id, now, ct);
                    }
                    await store.EnsureFolderAsync("campaign", ct);
                    await store.AppendAsync(StopThreads, TrackerCsvText.Encode($"{today:yyyy-MM-dd} {sent.ThreadId} {phrase}\r\n"), ct);
                }
            }
        }
        return $"{threads.Count} sent threads, {failures} failures read, {bounced} bounced, {replies} replies, {stops} stops{(readReplies ? "" : " (replies read on the hour)")}{trip}";
    }

    /// <summary>The stop phrase in a reply from one of the thread's other senders, read from the reply itself.</summary>
    private async Task<string?> StopAsync(SentThread sent, IReadOnlyList<string> others, CancellationToken ct)
    {
        foreach (string address in others)
        {
            foreach (string id in await inbox.FindAsync(RepliesFrom(address), 5, ct))
            {
                var message = await inbox.ReadAsync(id, ct);
                if (message.ThreadId == sent.ThreadId && StopPhrases.Find(message.Lines.Length > 0 ? message.Lines : message.Text) is { } phrase)
                {
                    return phrase;
                }
            }
        }
        return null;
    }
}
