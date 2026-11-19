// The quality-trend port answered by the reader that fills the Quality page's trend fences (QualityCounts), from the
// history file this build carries, and kept until that file changes: the promote writes one row a version, so the
// readings move once a version and every read between two writes is the same answer.
using System.Collections.Concurrent;

namespace TheYard.Api.Features.Docs;

// #region quality-trend-fence
/// <summary>One number's readings, read through the Quality page's own fence reader and kept until the history file changes.</summary>
/// <param name="repoRoot">The repository's root as this build carries it.</param>
public sealed class QualityTrendFence(string repoRoot) : IQualityTrend
{
    private readonly ConcurrentDictionary<string, (DateTime Written, IReadOnlyList<string> Lines)> _kept = new(StringComparer.Ordinal);

    /// <summary>The history file the readings come from.</summary>
    private string HistoryPath => Path.Combine(repoRoot, "data", "quality-history.json");

    /// <summary>The number's readings; an answer kept from the same write of the file is handed back as it is.</summary>
    /// <param name="number">The number's key in the history.</param>
    /// <param name="cancellation">Unused: the read is one small file, kept between writes.</param>
    public Task<IReadOnlyList<string>> ReadingsAsync(string number, CancellationToken cancellation)
    {
        var written = File.Exists(HistoryPath) ? File.GetLastWriteTimeUtc(HistoryPath) : DateTime.MinValue;
        if (_kept.TryGetValue(number, out var kept) && kept.Written == written)
        {
            return Task.FromResult(kept.Lines);
        }
        string fence = QualityCounts.Measure("quality-trend", number, repoRoot);
        // A measure the history lacks comes back as a note in words, in brackets: no readings, never the note as one.
        IReadOnlyList<string> lines = fence.StartsWith('(')
            ? []
            : [.. fence.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        _kept[number] = (written, lines);
        return Task.FromResult(lines);
    }
}
// #endregion quality-trend-fence
