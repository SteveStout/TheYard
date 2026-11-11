namespace JobHunt.Application.Features.Jobs;

/// <summary>The postings by when a scan first saw them, as the hiring signal reads them; it never writes them.</summary>
public interface IOpenings
{
    /// <summary>Each posting first seen at or after <paramref name="since"/>, open or closed: its company and title.</summary>
    Task<IReadOnlyList<(string Company, string Title)>> FirstSeenSinceAsync(DateTimeOffset since, CancellationToken ct);
}

/// <summary>
/// Writes the hiring signal (HiringSignal): the postings first seen in the last seven days, the companies among them
/// with three or more engineering seats, one line on the share that the TODAY card carries, renamed into place so the
/// card never reads half of it. Answers how many companies made the signal.
/// </summary>
/// <param name="store">The job-hunt share.</param>
/// <param name="openings">The postings, read only.</param>
/// <param name="time">The clock.</param>
public sealed class HiringSignals(IMergeStore store, IOpenings openings, TimeProvider time)
{
    /// <summary>One read and one line.</summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        var surges = HiringSignal.Surges(await openings.FirstSeenSinceAsync(now - HiringSignal.Window, ct));
        await store.EnsureFolderAsync("nightly", ct);
        await store.WriteAsync(MergePaths.Temp(HiringSignal.Path), TrackerCsvText.Encode(HiringSignal.Line(CentralClock.Local(now), surges) + "\n"), ct);
        await store.ReplaceAsync(MergePaths.Temp(HiringSignal.Path), HiringSignal.Path, ct);
        return surges.Count;
    }
}
