using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

/// <summary>
/// The daily card, at 5 past every hour (UTC clock). DAILY_CARD=on writes nightly/CARD.md when what it
/// says has changed, and at 6 AM Central whether or not it has, so the morning always has a fresh one;
/// anything else is off.
/// </summary>
public sealed class DailyCardTimer(IMergeStore store, IHttpClientFactory http, TimeProvider time, ILogger<DailyCardTimer> log)
{
    [Function("DailyCard")]
    public async Task RunAsync([TimerTrigger("0 5 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("DAILY_CARD"), "on", StringComparison.OrdinalIgnoreCase))
        {
            log.LogInformation("daily card idle: DAILY_CARD is not on");
            return;
        }
        log.LogInformation("daily card: {Result}", await WriteAsync(store, new DailyCardSource(store, http, time), time, ct));
    }

    /// <summary>Writes the card when its body changed or at 6 AM Central, and says which.</summary>
    public static async Task<string> WriteAsync(IMergeStore store, DailyCardSource source, TimeProvider time, CancellationToken ct)
    {
        var facts = await source.ReadAsync(ct);
        string card = DailyCard.Render(facts);
        string before = await store.ExistsAsync(DailyCard.Path, ct) ? TrackerCsvText.Decode(await store.ReadAsync(DailyCard.Path, ct)) : string.Empty;
        if (facts.At.Hour != 6 && DailyCard.Body(before) == DailyCard.Body(card))
        {
            return "unchanged";
        }
        await store.EnsureFolderAsync("nightly", ct);
        await store.WriteAsync(MergePaths.Temp(DailyCard.Path), TrackerCsvText.Encode(card), ct);
        await store.ReplaceAsync(MergePaths.Temp(DailyCard.Path), DailyCard.Path, ct);
        return "written";
    }
}
