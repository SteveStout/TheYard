using System.Collections.Concurrent;

namespace JobHunt.Infrastructure.Features.Merge;

/// <summary>
/// The folders this process has made sure of on the share, each remembered for an hour. The merger runs
/// every minute and makes sure of every folder it writes to, one create per path part, and the share bills
/// each of those creates: unremembered, that is a couple of thousand billed calls an hour whose whole answer
/// is "already exists". A folder made sure of is taken as there until the hour is up, which is the trade this
/// class exists to make: a folder moved away by hand is made again within the hour, and a write into it
/// fails until then rather than for good.
/// </summary>
public sealed class EnsuredFolders(TimeProvider clock)
{
    /// <summary>How long a folder made sure of is taken as there.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);

    /// <summary>True when the folder needs making sure of: never seen, or seen over an hour ago.</summary>
    public bool Needs(string path) =>
        !_seen.TryGetValue(path, out var at) || clock.GetUtcNow() - at >= Every;

    /// <summary>Remembers that the folder is there as of now.</summary>
    public void Seen(string path) => _seen[path] = clock.GetUtcNow();

    /// <summary>Forgets a folder, so the next write makes sure of it again (a write that found it gone).</summary>
    public void Forget(string path) => _seen.TryRemove(path, out _);
}
