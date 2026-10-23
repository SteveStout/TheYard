// What the Claude usage card reads (ADR: Every Claude lane reports its usage): the usage documents
// the lanes post, thirty days of them, read back from the log once a minute by the recorder and held
// for the card's view, which draws synchronously. The card's own minute keeps two counts, so the
// frame's live-read line and its staleness rule work for it the way they do for every other card.
using System.Globalization;
using TheYard.Application;
using TheYard.Domain;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>Reads the usage log for the Claude usage card and holds what it read.</summary>
/// <param name="log">The usage log.</param>
/// <param name="clock">This process's clock.</param>
public sealed class ClaudeSource(IUsageLog log, TimeProvider clock)
{
    /// <summary>How far back the card reads: its widest window.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    /// <summary>The two counts the card's minute keeps.</summary>
    public const string RunsToday = "runs_today", TokensOutToday = "tokens_out_today";

    /// <summary>The documents read last, newest first; empty before the first read. Replaced whole, never added to.</summary>
    private volatile IReadOnlyList<UsageRecord> _records = [];

    /// <summary>What the last read said about the log: where it is kept, or why it was not read.</summary>
    private volatile string _said = "not read yet";

    /// <summary>The documents of the last thirty days, newest first.</summary>
    public IReadOnlyList<UsageRecord> Records => _records;

    /// <summary>Where the documents are kept, or "not read: " and why.</summary>
    public string Said => _said;

    /// <summary>Reads the last thirty days back from the log.</summary>
    /// <param name="cancellation">Stops the read.</param>
    public async Task RefreshAsync(CancellationToken cancellation)
    {
        var availability = await log.AvailabilityAsync(cancellation);
        if (!availability.Available)
        {
            _said = "not read: " + availability.Reason;
            return;
        }
        _records = [.. (await log.SinceAsync(clock.GetUtcNow() - Window, cancellation)).OrderByDescending(record => record.At)];
        _said = availability.Reason;
    }

    /// <summary>One minute at the read's end: today's runs and tokens out, read fresh from the log.</summary>
    public async Task<IReadOnlyList<ResourceMinute>> MinutesAsync(DateTimeOffset from, DateTimeOffset to, string version, CancellationToken cancellation)
    {
        await RefreshAsync(cancellation);
        var today = NightlySlots.StartOfCentralDay(to);
        var counted = _records.Where(record => record.At >= today && !record.Dry).ToList();
        var utc = to.ToUniversalTime();
        return [new ResourceMinute(new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero), new Dictionary<string, double?>
        {
            [RunsToday] = counted.Count,
            [TokensOutToday] = counted.Sum(record => record.TokensOut),
        }, version)];
    }

    /// <summary>The hour's facts: where the documents are kept and how many the last read found.</summary>
    public async Task<ResourceHour> HourAsync(DateTimeOffset hour, DateTimeOffset since, CancellationToken cancellation)
    {
        await RefreshAsync(cancellation);
        return new ResourceHour(hour, "", null, new Dictionary<string, string?>
        {
            ["usage"] = _said,
            ["documents"] = _records.Count.ToString(CultureInfo.InvariantCulture),
        }, []);
    }
}
