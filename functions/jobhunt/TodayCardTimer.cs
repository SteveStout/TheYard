using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

// #region summary
/// <summary>
/// Writes the one card, TODAY.md at the share's root, at 7:30 Central every day, and mails it to Steve's own address
/// from his own Gmail, the token the campaign sends with. No model runs here.
/// </summary>
// #endregion summary
/// <remarks>
/// At 30 past every hour (UTC clock); acts only in the 7 o'clock hour Central (CentralClock.IsTodayHour), once a day:
/// a day whose line in nightly/today-card.log already says "mailed" is left as it was. TODAY_CARD picks the mode:
/// unset or "on" writes the card and mails it, "write" writes it and mails nothing, "off" does neither. Unset is on
/// so that a template redeploy that drops the setting cannot silently stop the card. The mail goes to one fixed
/// address, his own (TodayMail.To), and to nobody else. POST /api/today/hand-run with the function key runs the same
/// write and mail at any hour, for a hand run. One line a run goes to nightly/today-card.log.
/// </remarks>
public sealed class TodayCardTimer(IMergeStore store, IHttpClientFactory http, TimeProvider time, ILogger<TodayCardTimer> log)
{
    /// <summary>The card's log on the share.</summary>
    public const string Log = "nightly/today-card.log";

    /// <summary>The hourly tick: in the 7 o'clock hour Central, once a day, writes the card and mails it unless the switch says otherwise.</summary>
    [Function("TodayCard")]
    public async Task RunAsync([TimerTrigger("0 30 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var run = new RunLine("TodayCard", now, [30]);
        try
        {
            string mode = Mode(Environment.GetEnvironmentVariable("TODAY_CARD"));
            if (mode == "off" || !CentralClock.IsTodayHour(now))
            {
                run.Came("unchanged", mode == "off" ? "switch_off" : "not_the_hour");
                return;
            }
            if (await MailedTodayAsync(store, CentralClock.Local(now), ct))
            {
                run.Came("unchanged", "mailed_already");
                return;
            }
            string line = await WriteAsync(store, new TodayCardSource(store, http, time), mode == "on" ? Sender() : null, time, "07:30", ct, LabelsAsync);
            run.Count("written", 1).Count("mailed", mode == "on" ? 1 : 0).Came("success", mode);
            log.LogInformation("today card: {Line}", line);
        }
        catch (Exception ex)
        {
            run.Failed(ex);
            throw;
        }
        finally
        {
            RunLines.Write(log, run, time.GetUtcNow());
        }
    }

    /// <summary>The hand run: the same write and mail at any hour, answered with the log line.</summary>
    [Function("TodayCardHandRun")]
    public async Task<HttpResponseData> HandRunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "today/hand-run")] HttpRequestData request,
        CancellationToken ct)
    {
        var run = new RunLine("TodayCardHandRun", time.GetUtcNow());
        try
        {
            string line = await WriteAsync(store, new TodayCardSource(store, http, time), Sender(), time, "hand run", ct, LabelsAsync);
            run.Count("written", 1).Count("mailed", 1).Came("success", "hand");
            var response = request.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
            response.Headers.Add("Cache-Control", "no-store");
            await response.WriteStringAsync(line, ct);
            return response;
        }
        catch (Exception ex)
        {
            run.Failed(ex);
            throw;
        }
        finally
        {
            RunLines.Write(log, run, time.GetUtcNow());
        }
    }

    /// <summary>
    /// Draws the card, replaces TODAY.md whole, mails it when a sender is given, reads the sent message's Gmail labels
    /// back when a reader is given (SENT is the Sent folder, INBOX is his inbox receiving it), and logs one line; the
    /// line is the answer. A mail that fails after the write leaves the card written and its log line saying so, then throws.
    /// </summary>
    public static async Task<string> WriteAsync(IMergeStore store, TodayCardSource source, IMailSender? sender, TimeProvider time, string kind, CancellationToken ct, Func<string, CancellationToken, Task<string>>? labels = null)
    {
        var inputs = await source.ReadAsync(ct);
        string card = TodayCard.Render(inputs);
        byte[] bytes = TrackerCsvText.Encode(card);
        await store.WriteAsync(MergePaths.Temp(TodayCard.Path), bytes, ct);
        await store.ReplaceAsync(MergePaths.Temp(TodayCard.Path), TodayCard.Path, ct);
        string head = $"{CentralClock.Local(time.GetUtcNow()):yyyy-MM-dd HH:mm:ss} {kind}: written {bytes.Length} B, {TodayCard.Minutes(card)} min";
        string line;
        if (sender is null)
        {
            line = head + ", not mailed (TODAY_CARD=write)";
        }
        else
        {
            try
            {
                var sent = await sender.SendAsync(new OutgoingMail(TodayMail.To, TodayCard.Subject(inputs.At), card, TodayMail.Html(card)), ct);
                line = $"{head}, mailed to {TodayMail.To}, gmail {sent.MessageId} thread {sent.ThreadId}";
                if (labels is not null)
                {
                    line += ", labels " + await labels(sent.MessageId, ct);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                await store.AppendAsync(Log, TrackerCsvText.Encode($"{head}, mail FAILED: {ex.GetType().Name}\r\n"), ct);
                throw;
            }
        }
        await store.AppendAsync(Log, TrackerCsvText.Encode(line + "\r\n"), ct);
        return line;
    }

    /// <summary>Whether the log holds a line for this Central day, from the timer, that says the card was mailed.</summary>
    public static async Task<bool> MailedTodayAsync(IShareReader store, DateTime local, CancellationToken ct)
    {
        if (!await store.ExistsAsync(Log, ct))
        {
            return false;
        }
        string day = local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return TodayParts.Lines(TrackerCsvText.Decode(await store.ReadAsync(Log, ct)))
            .Any(l => l.StartsWith(day, StringComparison.Ordinal) && l.Contains(" 07:30: ", StringComparison.Ordinal) && l.Contains(", mailed to ", StringComparison.Ordinal));
    }

    /// <summary>The switch's value: "write" and "off", any case, and everything else, unset included, "on".</summary>
    public static string Mode(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "off" => "off",
        "write" => "write",
        _ => "on",
    };

    /// <summary>The sent message's label ids as Gmail holds them, read with the same token, or why they were not read.</summary>
    private async Task<string> LabelsAsync(string id, CancellationToken ct)
    {
        try
        {
            using var client = await GoogleCredentials.FromEnvironment()!.ClientAsync(http, ct);
            using var json = JsonDocument.Parse(await client.GetStringAsync($"https://gmail.googleapis.com/gmail/v1/users/me/messages/{id}?format=minimal", ct));
            return string.Join(",", json.RootElement.GetProperty("labelIds").EnumerateArray().Select(l => l.GetString()).Order(StringComparer.Ordinal));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return $"not read ({ex.GetType().Name})";
        }
    }

    private IMailSender Sender() =>
        GoogleCredentials.FromEnvironment() is { } google ? new GmailSender(http, google) : throw new InvalidOperationException("the Google settings are not set, so the card cannot be mailed");
}
