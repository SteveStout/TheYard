using System.Globalization;
using JobHunt.Application.Shared.Walls;
using JobHunt.Domain.Shared.Companies;

namespace JobHunt.Application.Features.NetworkLog;

/// <summary>NETWORK_SEND: whether an email reconnect sends itself. Shadow, the default, sends nothing.</summary>
public enum SendMode
{
    /// <summary>Each reconnect is written as a Gmail draft for Steve, and as the HTML that would send under networklog/would-sends.</summary>
    Shadow,

    /// <summary>Each reconnect goes out through the campaign's mail port, read back from Sent before it counts.</summary>
    On,
}

/// <summary>What one Monday's email reconnects did.</summary>
/// <param name="Sent">Sent and read back from Sent.</param>
/// <param name="Drafted">Written as drafts, in shadow.</param>
/// <param name="Held">Held by a guard, by the guard.</param>
/// <param name="Failed">Rows whose render failed a check or whose send did not read back, each with the reason.</param>
public sealed record ReconnectResult(int Sent, int Drafted, IReadOnlyDictionary<string, int> Held, IReadOnlyList<string> Failed);

/// <summary>
/// The week's email reconnects in RECONNECT-v1, which send themselves once NETWORK_SEND reads on. Each due email row passes the guards with its thread read now, gets one code the
/// make_ref.py way, and is rendered with the second question its tracker rows call for. In shadow it is written as a
/// draft into its thread, as before, and as the HTML that would send; in `on` it goes out through the campaign's
/// mail port into its thread, counts only once Gmail shows it under Sent, and is recorded: the row marked with the
/// send, the code's spool row, a tracker-inbox fragment and the day's sent file the 07:30 card reads. The code is
/// spooled only for a draft that was written or a send that read back.
/// </summary>
public sealed class Reconnects(IConversationStore rows, IThreadReader gmail, IDraftWriter drafts, IMergeStore share, TimeProvider time)
{
    /// <summary>The prefix a sent reconnect's DraftId carries, before the Gmail message id.</summary>
    public const string SentPrefix = "sent:";

    /// <summary>NETWORK_SEND's mode.</summary>
    public SendMode Mode { get; init; } = SendMode.Shadow;

    /// <summary>The campaign's sender, which `on` reaches; shadow is never handed one that can send.</summary>
    public IMailSender? Mail { get; init; }

    /// <summary>The Sent read-back.</summary>
    public ISentReader? SentBox { get; init; }

    /// <summary>The wait between two real sends, the campaign's five seconds; the tests set it to nothing.</summary>
    public TimeSpan Spacing { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The label every draft carries, made once a run.</summary>
    private string? _label;

    /// <summary>Runs the due email rows of a Central day.</summary>
    public async Task<ReconnectResult> RunAsync(DateOnly day, IReadOnlyList<Conversation> due, IReadOnlyList<Conversation> all, DateTimeOffset now, CancellationToken ct)
    {
        var held = new Dictionary<string, int>(StringComparer.Ordinal);
        var failed = new List<string>();
        if (due.Count == 0)
        {
            return new ReconnectResult(0, 0, held, failed);
        }
        var blacklist = Blacklist.Read(await TextAsync(Blacklist.File, ct));
        var master = TrackerCsvText.Parse(await TextAsync(MergePaths.Master, ct)).Where(r => r.Length >= 7).ToList();
        var taken = await TakenAsync(ct);
        var sentTo = all.Where(r => r.DraftId?.StartsWith(SentPrefix, StringComparison.Ordinal) == true && r.DraftedAt is { } at && now - at < TimeSpan.FromDays(ReconnectGuards.CompanyDays))
            .Select(r => Company(r)).ToList();
        int sent = 0, drafted = 0;
        foreach (var row in due)
        {
            var thread = await gmail.ReadSummaryAsync(row.ThreadIds[0], ct);
            string company = Company(row);
            int toCompany = sentTo.Count(c => c.Length > 0 && c == company);
            if (ReconnectGuards.Hold(row, new ReconnectContext(thread, blacklist, toCompany, sent, now)) is { } why)
            {
                held[why] = held.GetValueOrDefault(why) + 1;
                continue;
            }
            string code = RefCodes.New(taken);
            var question = ReconnectTemplate.QuestionFor(row.Company ?? row.CompanyKey, master, day);
            var mail = ReconnectTemplate.Render(row.FirstName, row.TopicLine ?? "", row.LastSubject ?? thread.Subject, question, code);
            if (ReconnectTemplate.Check(mail, code) is { Count: > 0 } checks)
            {
                failed.Add($"{row.PersonKey}: {string.Join(", ", checks)}");
                continue;
            }
            string? newest = thread.Messages.LastOrDefault(m => m.RfcMessageId is not null)?.RfcMessageId;
            if (Mode == SendMode.On)
            {
                if (sent > 0)
                {
                    await Task.Delay(Spacing, time, ct);
                }
                bool went;
                try
                {
                    went = await SendOneAsync(day, row, mail, code, question, newest, ct);
                }
                catch (MailHeldException stopped)
                {
                    // A no-email day (NoEmailDayGate): the row is left as it was, so the next open day sends it.
                    held[stopped.Reason] = held.GetValueOrDefault(stopped.Reason) + 1;
                    break;
                }
                if (!went)
                {
                    failed.Add($"{row.PersonKey}: not in Sent");
                    continue;
                }
                sentTo.Add(company);
                sent++;
                continue;
            }
            await WouldSendAsync(day, row, mail, ct);
            if (await DraftAsync(row, mail, newest, ct) is not { } draftId)
            {
                failed.Add($"{row.PersonKey}: draft not read back");
                continue;
            }
            await rows.SetDraftAsync(row.Id, draftId, Channel.Email, now, ct);
            await SpoolCodeAsync(row, code, row.ThreadIds[0], time.GetUtcNow(), ct);
            drafted++;
        }
        return new ReconnectResult(sent, drafted, held, failed);
    }

    /// <summary>
    /// A bounced reconnect sent again to the address Hunter verified instead: the same text with a
    /// fresh code, into the same thread, read back from Sent. Only `on` sends; in shadow it answers false.
    /// </summary>
    /// <param name="day">The Central day.</param>
    /// <param name="row">The row, its Email the new address.</param>
    /// <param name="question">The second question the first send asked.</param>
    public async Task<bool> ResendAsync(DateOnly day, Conversation row, ReconnectQuestion question, CancellationToken ct)
    {
        if (Mode != SendMode.On)
        {
            return false;
        }
        var thread = await gmail.ReadSummaryAsync(row.ThreadIds[0], ct);
        string code = RefCodes.New(await TakenAsync(ct));
        var mail = ReconnectTemplate.Render(row.FirstName, row.TopicLine ?? "", row.LastSubject ?? thread.Subject, question, code);
        return ReconnectTemplate.Check(mail, code).Count == 0
            && await SendOneAsync(day, row, mail, code, question, thread.Messages.LastOrDefault(m => m.RfcMessageId is not null)?.RfcMessageId, ct);
    }

    /// <summary>
    /// A reconnect that bounced: Hunter is asked once for the person at the address's domain, and an address it verifies
    /// that differs from the one that bounced gets the same reconnect, sent again, and becomes the row's. Otherwise the
    /// row closes as bounced and the answer is false.
    /// </summary>
    public async Task<bool> BouncedAsync(Conversation row, IAddressFinder? finder, DateTimeOffset now, CancellationToken ct)
    {
        string domain = row.Email is { } address && address.Contains('@', StringComparison.Ordinal) ? address[(address.IndexOf('@', StringComparison.Ordinal) + 1)..] : row.CompanyKey ?? "";
        FoundAddress? found = null;
        try
        {
            found = finder is null || domain.Length == 0 ? null : await finder.FindAsync(row.FullName, domain, ct);
        }
        catch (FinderRefusedException)
        {
            found = null;
        }
        if (found is not { } other || string.Equals(other.Address, row.Email, StringComparison.OrdinalIgnoreCase)
            || !await ResendAsync(DateOnly.FromDateTime(CentralClock.Local(now)), row with { Email = other.Address }, ReconnectQuestion.Remote, ct))
        {
            await rows.SetStateAsync(row.Id, ConversationState.Closed, "bounce", ct);
            return false;
        }
        await rows.UpsertAsync((await rows.FindAsync(row.PersonKey, ct) ?? row) with { Email = other.Address }, now, ct);
        return true;
    }

    /// <summary>One send through the campaign's port into the row's thread, counted only once Sent shows it, then recorded.</summary>
    private async Task<bool> SendOneAsync(DateOnly day, Conversation row, FollowUpDraft mail, string code, ReconnectQuestion question, string? newest, CancellationToken ct)
    {
        if (Mail is not { } sender || SentBox is not { } box)
        {
            throw new InvalidOperationException("NETWORK_SEND on was given no mail port");
        }
        var result = await sender.SendAsync(new OutgoingMail(row.Email!, mail.Subject, mail.Text, mail.Html) { ThreadId = row.ThreadIds[0], InReplyTo = newest }, ct);
        if (!await box.InSentAsync(result.MessageId, ct))
        {
            return false;
        }
        var at = time.GetUtcNow();
        await rows.SetDraftAsync(row.Id, SentPrefix + result.MessageId, Channel.Email, at, ct);
        await RecordAsync(day, row, code, question, result.ThreadId, result.MessageId, at, ct);
        return true;
    }

    /// <summary>The company a row's sends count against: its registrable domain, else its name, lower case.</summary>
    public static string Company(Conversation row) => (row.CompanyKey ?? row.Company ?? string.Empty).Trim().ToLowerInvariant();

    private async Task<string?> DraftAsync(Conversation row, FollowUpDraft mail, string? newest, CancellationToken ct)
    {
        if (_label is null)
        {
            await drafts.EnsureLabelAsync(MondayDrafts.Parent, ct);
            _label = await drafts.EnsureLabelAsync(MondayDrafts.Ready, ct);
        }
        var (draftId, messageId) = await drafts.CreateDraftAsync(new DraftMail(row.ThreadIds[0], row.Email!, mail.Subject, mail.Text, mail.Html, newest, newest), ct);
        await drafts.LabelAsync(messageId, _label, ct);
        return await drafts.DraftExistsAsync(draftId, ct) ? draftId : null;
    }

    /// <summary>The exact HTML that would send, one file a row under the day's would-sends folder.</summary>
    private async Task WouldSendAsync(DateOnly day, Conversation row, FollowUpDraft mail, CancellationToken ct)
    {
        string folder = NetworkFiles.WouldSendsFolder(day);
        await share.EnsureFolderAsync(NetworkFiles.WouldSends, ct);
        await share.EnsureFolderAsync(folder, ct);
        string file = $"{NetworkFiles.Safe(row.ThreadIds[0])}.html";
        await share.WriteAsync($"{folder}/{file}", TrackerCsvText.Encode(NetworkFiles.WouldSendText(row, mail)), ct);
        await share.AppendAsync(NetworkFiles.WouldSendsIndex(day), TrackerCsvText.Encode($"{file}\t{row.FullName.Replace('\t', ' ')}\t{row.Email}\t{mail.Subject.Replace('\t', ' ')}\n"), ct);
    }

    /// <summary>A send's records on the share: the code's row, the tracker fragment and the day's sent line.</summary>
    private async Task RecordAsync(DateOnly day, Conversation row, string code, ReconnectQuestion question, string threadId, string messageId, DateTimeOffset at, CancellationToken ct)
    {
        await SpoolCodeAsync(row, code, threadId, at, ct);
        var local = CentralClock.Local(at);
        var inv = CultureInfo.InvariantCulture;
        string stamp = at.UtcDateTime.ToString("yyyyMMdd-HHmmss", inv) + "Z";
        string note = $"SENT by NetworkLogPass {local.ToString("h:mm tt", inv)} CST {local.ToString("M/d", inv)}: email reconnect {ReconnectTemplate.Version} ({question} question), Gmail msg {messageId}, thread {threadId}, code {code}, read back in Sent.";
        await share.EnsureFolderAsync(NetworkFiles.TrackerSpool, ct);
        await share.WriteAsync($"{NetworkFiles.TrackerSpool}/{stamp}-networklog-{messageId}.csv", TrackerCsvText.Encode(
            "Person,Company,Communication,Date,Phone/Email,Notes\r\n" + TrackerCsvText.Line([row.FullName, row.Company ?? row.CompanyKey ?? "", "Email", local.ToString("M/d/yyyy", inv), row.Email!, note]) + "\r\n"), ct);
        await share.EnsureFolderAsync(NetworkFiles.Sent, ct);
        await share.AppendAsync(NetworkFiles.SentPath(day), TrackerCsvText.Encode(NetworkFiles.SentLine(local, row, threadId, messageId, code)), ct);
    }

    private async Task SpoolCodeAsync(Conversation row, string code, string threadId, DateTimeOffset at, CancellationToken ct)
    {
        var inv = CultureInfo.InvariantCulture;
        string stamp = at.UtcDateTime.ToString("yyyyMMdd-HHmmss", inv) + "Z";
        await share.EnsureFolderAsync(RefCodes.Spool, ct);
        await share.WriteAsync($"{RefCodes.Spool}/{stamp}-{RefCodes.Lane}-{code}.csv", TrackerCsvText.Encode(
            RefCodes.Header + "\r\n" + TrackerCsvText.Line([code, row.FullName, row.Company ?? row.CompanyKey ?? "", "Email", "", threadId, at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", inv) + "Z", RefCodes.Lane]) + "\r\n"), ct);
    }

    /// <summary>Every code link-tokens.csv and its spool hold, so a new one is new.</summary>
    private async Task<HashSet<string>> TakenAsync(CancellationToken ct)
    {
        var taken = new HashSet<string>(RefCodes.In(await TextAsync(RefCodes.Tokens, ct)), StringComparer.Ordinal);
        foreach (var entry in (await share.ListFilesAsync(RefCodes.Spool, ct)).Where(e => e.Name.EndsWith(".csv", StringComparison.Ordinal)))
        {
            taken.UnionWith(RefCodes.In(TrackerCsvText.Decode(await share.ReadAsync($"{RefCodes.Spool}/{entry.Name}", ct))));
        }
        return taken;
    }

    private async Task<string> TextAsync(string path, CancellationToken ct) =>
        await share.ExistsAsync(path, ct) ? TrackerCsvText.Decode(await share.ReadAsync(path, ct)) : string.Empty;
}
