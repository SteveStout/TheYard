// The Admin tab's four public lists, kept in the document store and read back by card and window.
// The writer and the reader share one file because they share one region, quoted live on the
// site, and because each is the other's other half: what Keep writes is what ReadAsync serves.
// The collector both of them use is in Logs.cs (ADR: Logs that outlive the container).
using System.Collections.Concurrent;
using System.Text.Json;
using TheYard.Application;

namespace TheYard.Api;

// #region kept-rings
/// <summary>
/// The Admin tab's four public lists, kept where a roll cannot empty them
/// (ADR: Logs that outlive the container, the addendum on the cards). Each ring hands every entry it takes to
/// <see cref="Keep{T}"/>, which writes it as the JSON the ring's own endpoint
/// serves and offers it to the same collector the kept log uses: one channel,
/// one drain a minute, one transactional batch a day partition, and nothing on
/// a request thread waits for the store.
/// </summary>
public sealed class KeptRingWriter(LogCollector collector, string site)
{
    /// <summary>Snake case, as every endpoint on this site answers, so a kept entry and a ring entry are the same text.</summary>
    public static readonly JsonSerializerOptions Wire = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>
    /// Serializes one ring entry the way the ring's endpoint serves it and
    /// offers it to the collector, leaving out any entry too long to keep.
    /// </summary>
    public void Keep<T>(string kind, DateTimeOffset at, T entry)
    {
        string json = JsonSerializer.Serialize(entry, Wire);
        if (json.Length <= KeptRings.MostCharacters)
        {
            collector.Offer(KeptRings.Entry(kind, site, at, json));
        }
    }
}

/// <summary>
/// A card's window read back. Public, like the rings, because it serves what
/// the rings serve; cached for half a minute a card and window, because a
/// public endpoint that runs a query in the store on every call is an
/// invitation, and thirty seconds is the rate the tab refreshes at anyway.
/// </summary>
public sealed class KeptRingReader(LogCollector collector, string site, string storeName)
{
    /// <summary>The most entries one card's answer carries.</summary>
    public const int Most = 200;

    /// <summary>
    /// How long a cached answer is served before the store is asked again. Half
    /// a minute, or the collector's own interval where that is shorter: an
    /// answer cannot change faster than the store is written to, and the
    /// browser suite writes every two seconds so that it can wait for a line.
    /// </summary>
    private TimeSpan Fresh => TimeSpan.FromSeconds(Math.Min(30, collector.Interval.TotalSeconds));

    /// <summary>Answers by card and window, each with the moment it was made.</summary>
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, object Answer)> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// One card's window: from the cache while it is fresh, from the store
    /// otherwise, and null when the card or the window is not one this site
    /// knows.
    /// </summary>
    public async Task<object?> ReadAsync(string? card, string? window, DateTimeOffset now, CancellationToken cancellation)
    {
        if (card is null || !KeptRings.ByCard.TryGetValue(card, out string? kind) || ActivityWindows.Parse(window) is not { } chosen)
        {
            return null;
        }

        string key = card + ":" + chosen.Name;
        if (_cache.TryGetValue(key, out var held) && now - held.At < Fresh)
        {
            return held.Answer;
        }

        DateTimeOffset since = now - chosen.Length;
        var availability = await collector.Store.AvailabilityAsync(cancellation);
        var page = availability.Available
            ? await collector.Store.RingAsync(kind, site, since, Most, cancellation)
            : new KeptRingPage([], 0);
        object answer = new
        {
            card,
            window = chosen.Name,
            site,
            store = storeName,
            since,
            until = now,
            // The container keeps the keyed log for years; a ring entry carries
            // its own, shorter life, and that is the one this card is about.
            kept = new
            {
                available = availability.Available,
                note = availability.Available ? $"kept in Azure Cosmos DB for {KeptRings.RetentionSeconds / 86_400} days" : availability.Reason,
            },
            total = page.Total,
            shown = page.Entries.Count,
            entries = page.Entries.Select(Parsed).ToList(),
        };
        _cache[key] = (now, answer);
        return answer;
    }

    /// <summary>A kept entry's JSON as an element, cloned so it outlives the document it was parsed from.</summary>
    private static JsonElement Parsed(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
// #endregion kept-rings
