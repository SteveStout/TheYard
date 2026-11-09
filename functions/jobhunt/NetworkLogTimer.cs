using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

// #region summary
/// <summary>
/// Logs every two-way conversation in the 6 o'clock hour Central, once a Central day: Gmail threads into
/// network.Conversations, and on a Monday the week's follow-ups. In shadow it writes what it would draft to the share
/// and calls no draft service; nothing in it can send.
/// </summary>
// #endregion summary
/// <remarks>
/// Every hour on the hour (UTC); acts only in the 6 o'clock hour Central (CentralClock.IsNetworkHour), or at any hour
/// while NETWORK_LOG_ANY_HOUR is "on", or at any hour of a Monday whose pass ran in shadow before the switch read "on". NETWORK_LOG picks the mode: "shadow", "on", anything else off. CAMPAIGN_SQL is
/// the database the network schema lives in; without it the pass is blocked, and without the schema it is blocked
/// with no_table, one line on the share saying so. A day counts as run once its network.Passes row has FinishedAt; a
/// row begun two hours ago with none is a crashed pass, and the hour runs it again.
/// </remarks>
public sealed class NetworkLogTimer(IMergeStore store, IHttpClientFactory http, TimeProvider time, ILogger<NetworkLogTimer> log)
{
    /// <summary>How long an unfinished pass row holds the day before it counts as crashed.</summary>
    public static readonly TimeSpan Crashed = TimeSpan.FromHours(2);

    /// <summary>One hourly tick: the switch, the hour, the database and the day's row, then the pass.</summary>
    [Function("NetworkLogPass")]
    public async Task RunAsync([TimerTrigger("0 0 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var run = new RunLine("NetworkLogPass", now, [0]);
        try
        {
            var mode = Mode(Environment.GetEnvironmentVariable("NETWORK_LOG"));
            if (mode == NetworkMode.Off)
            {
                run.Came("unchanged", "switch_off");
                return;
            }
            bool anyHour = string.Equals(Environment.GetEnvironmentVariable("NETWORK_LOG_ANY_HOUR"), "on", StringComparison.OrdinalIgnoreCase);
            bool hour = anyHour || CentralClock.IsNetworkHour(now);
            if (!hour && !(mode == NetworkMode.On && CentralClock.Local(now).DayOfWeek == DayOfWeek.Monday))
            {
                run.Came("unchanged", "not_the_hour");
                return;
            }
            string? sql = Environment.GetEnvironmentVariable("CAMPAIGN_SQL");
            if (string.IsNullOrWhiteSpace(sql))
            {
                run.Came("blocked", "no_database");
                return;
            }
            if (GoogleCredentials.FromEnvironment() is not { } google)
            {
                run.Came("blocked", "no_google");
                return;
            }
            var database = new SqlConversationStore(sql);
            var day = DateOnly.FromDateTime(CentralClock.Local(now));
            PassRow? today;
            try
            {
                today = await database.PassAsync(day, ct);
            }
            catch (Exception ex) when (SqlConversationStore.IsMissingTable(ex))
            {
                await store.EnsureFolderAsync(NetworkFiles.Folder, ct);
                await store.AppendAsync(NetworkFiles.PassLog, TrackerCsvText.Encode($"{CentralClock.Local(now):yyyy-MM-dd HH:mm:ss} blocked: no_table (the network schema is not published)\n"), ct);
                run.Came("blocked", "no_table");
                return;
            }
            string modeName = mode == NetworkMode.On ? "on" : "shadow";
            if (Held(today, modeName, now) is { } held)
            {
                run.Came("unchanged", held);
                return;
            }
            if (!hour && !Flipped(today, modeName))
            {
                run.Came("unchanged", "not_the_hour");
                return;
            }
            await database.StartPassAsync(day, modeName, now, ct);
            IDraftWriter drafts = mode == NetworkMode.On ? new GmailDrafts(http, google) : new NoDraftWriter();
            var result = await new NetworkLogPass(database, new GmailThreads(http, google, time), drafts, store, time) { Posts = new BlogFeed(http) }.RunAsync(mode, await database.OpenThreadIdsAsync(ct), ct);
            await store.AppendAsync(NetworkFiles.PassLog, TrackerCsvText.Encode(result.Line + "\n"), ct);
            await database.FinishPassAsync(day, time.GetUtcNow(), result.Counts, ct);
            log.LogInformation("network pass: {Line}", result.Line);
            run.Count("threads", result.Counts.ThreadsRead).Count("upserted", result.Counts.Upserted).Count("drafted", result.Counts.DraftsBuilt)
                .Came(result.Cut > 0 ? "late" : "success", modeName);
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
    /// Why the day's pass does not run now, or null when it should: already finished in this mode, or still running.
    /// A day finished in shadow runs once more when the switch reads `on`, so a Monday flipped after the 6 o'clock pass
    /// builds its real drafts the next hour; the ingest is idempotent, and a row is drafted only once.
    /// </summary>
    public static string? Held(PassRow? today, string mode, DateTimeOffset now) => today switch
    {
        null => null,
        { FinishedAt: not null } when today.Mode == mode => "already_ran",
        { FinishedAt: not null } => null,
        _ when now - today.StartedAt < Crashed => "running",
        _ => null,
    };

    /// <summary>
    /// Whether a Monday pass runs outside the 6 o'clock hour: only when the switch reads `on` and the day's pass finished
    /// in shadow, so a Monday flipped after the shadow pass builds its drafts at the next hour, once.
    /// </summary>
    public static bool Flipped(PassRow? today, string mode) => mode == "on" && today is { FinishedAt: not null, Mode: "shadow" };

    /// <summary>The switch's value as a mode: "on" and "shadow", any case, and everything else off.</summary>
    public static NetworkMode Mode(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "on" => NetworkMode.On,
        "shadow" => NetworkMode.Shadow,
        _ => NetworkMode.Off,
    };
}

/// <summary>The draft port shadow gets: every call refuses, so a shadow path that reached it would fail loudly.</summary>
public sealed class NoDraftWriter : IDraftWriter
{
    /// <inheritdoc />
    public Task<string> EnsureLabelAsync(string name, CancellationToken ct) => throw new InvalidOperationException("shadow writes no draft");

    /// <inheritdoc />
    public Task<(string DraftId, string MessageId)> CreateDraftAsync(DraftMail draft, CancellationToken ct) => throw new InvalidOperationException("shadow writes no draft");

    /// <inheritdoc />
    public Task LabelAsync(string messageId, string labelId, CancellationToken ct) => throw new InvalidOperationException("shadow writes no draft");

    /// <inheritdoc />
    public Task<bool> DraftExistsAsync(string draftId, CancellationToken ct) => throw new InvalidOperationException("shadow writes no draft");
}
