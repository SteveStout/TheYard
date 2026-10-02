// What a performance proof run hands back: one cell per store per path, one row per path,
// and the result that holds them with the verdict on each row and the sentence that sums the
// run up. Kept apart from the runner because this is the part the Admin card reads and the
// part that decides what "the same" means.
namespace TheYard.Api;

// #region proof-result
/// <summary>One path on one store: the samples, the medians, and what the store did per request.</summary>
/// <param name="Store">The store's display name.</param>
/// <param name="Samples">How many requests on the path answered below 400.</param>
/// <param name="P50Ms">The median duration of those requests, in milliseconds.</param>
/// <param name="P95Ms">The 95th percentile duration of those requests, in milliseconds.</param>
/// <param name="OperationsPerRequest">The average number of statements or store operations per request, to one decimal place.</param>
/// <param name="RequestUnitsPerRequest">The average request units per request, or null on a store with no such unit.</param>
/// <param name="Failures">How many requests on the path answered 400 or above.</param>
public sealed record ProofCell(
    string Store,
    int Samples,
    long P50Ms,
    long P95Ms,
    double OperationsPerRequest,
    double? RequestUnitsPerRequest,
    int Failures);

/// <summary>
/// One row of the proof: a path, both stores, and the median of the paired
/// differences, the second store's time minus the first's per round, so a
/// negative number means the second store was faster. The second difference
/// is the same with one round trip per operation taken off each side.
/// </summary>
/// <param name="Path">The key of the path, such as sign_in or listing.</param>
/// <param name="Label">The name the card shows for the path.</param>
/// <param name="Cells">One cell per store, in store order.</param>
/// <param name="MedianDifferenceMs">The median of the second store's time minus the first's per round, in milliseconds; null when nothing could be paired.</param>
/// <param name="DifferenceWithoutHopsMs">The same difference with one round trip per operation taken off each side, in milliseconds; null when nothing could be paired.</param>
/// <param name="Verdict">One phrase for the row, such as the same, not measured, or which store leads and by how much.</param>
public sealed record ProofRow(
    string Path,
    string Label,
    IReadOnlyList<ProofCell> Cells,
    long? MedianDifferenceMs,
    long? DifferenceWithoutHopsMs,
    string Verdict);

/// <summary>One store in a proof run, and its measured round trip.</summary>
/// <param name="Key">The store's key.</param>
/// <param name="Name">The store's display name.</param>
/// <param name="HopMs">The median round trip to the store, in milliseconds, or null when it was not measured.</param>
public sealed record ProofStore(string Key, string Name, long? HopMs);

/// <summary>The outcome of one proof run: whether it finished, when, on which stores, every row, and the sentence that sums it up.</summary>
/// <param name="Status">done or failed.</param>
/// <param name="Reason">Why the run failed, or null when it finished.</param>
/// <param name="StartedAt">When the run started, or null when it failed before starting.</param>
/// <param name="FinishedAt">When the run finished or failed.</param>
/// <param name="Rounds">How many paired rounds were run.</param>
/// <param name="Stores">The stores compared, in order.</param>
/// <param name="Rows">One row per path.</param>
/// <param name="Sentence">The one sentence that sums up the run.</param>
public sealed record ProofResult(
    string Status,
    string? Reason,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int Rounds,
    IReadOnlyList<ProofStore> Stores,
    IReadOnlyList<ProofRow> Rows,
    string Sentence)
{
    /// <summary>
    /// Which way round both difference columns are taken, in words, such as
    /// "Azure Cosmos DB minus Azure SQL Database": the second store's time
    /// minus the first's, so a negative difference means the second store
    /// answered faster. The card puts it in the column's header, because a
    /// signed number with no direction cannot be read. Empty when the run
    /// names fewer than two stores and so has no difference.
    /// </summary>
    public string DifferenceOrder => Stores.Count < 2 ? "" : $"{Stores[1].Name} minus {Stores[0].Name}";

    /// <summary>A run that could not finish: the reason doubles as the sentence, and every store is named with no round trip.</summary>
    public static ProofResult Failed(string reason, Backends backends) => new(
        "failed",
        reason,
        null,
        DateTimeOffset.UtcNow,
        0,
        backends.All.Select(backend => new ProofStore(backend.Key, backend.Name, null)).ToArray(),
        [],
        reason);

    // #region verdict
    /// <summary>
    /// How much the two stores are allowed to differ and still be called the
    /// same: fifteen milliseconds or fifteen per cent of the slower one,
    /// whichever is more. Fifteen milliseconds is about the spread between
    /// two rounds of the same request on the same store.
    /// </summary>
    public static bool Same(long difference, long slower) =>
        Math.Abs(difference) <= Math.Max(15, slower * 0.15);

    /// <summary>
    /// A finished run summed up: for registration and every path, a cell per
    /// store, the median of the paired differences (round by round, second
    /// store minus first), the same with each side's round trips taken off,
    /// and the row's verdict; then the sentence over all the rows.
    /// </summary>
    public static ProofResult Of(DateTimeOffset started, int rounds, IReadOnlyList<ProofRunner.StoreRun> stores)
    {
        var first = stores[0];
        var second = stores[1];
        var rows = new List<ProofRow>();
        foreach (var (path, label) in ProofRunner.Paths.Prepend(("register", "Register")))
        {
            var cells = stores.Select(store => Cell(store, path)).ToArray();
            var a = first.Samples.Where(s => s.Path == path && s.Status < 400).Select(s => s.Ms).ToArray();
            var b = second.Samples.Where(s => s.Path == path && s.Status < 400).Select(s => s.Ms).ToArray();
            int pairs = Math.Min(a.Length, b.Length);
            long? difference = pairs == 0 ? null : Percentiles.Of(Enumerable.Range(0, pairs).Select(i => b[i] - a[i]).ToArray(), 50);
            long? withoutHops = difference is null
                ? null
                : difference - (long)Math.Round(
                    (cells[1].OperationsPerRequest * (second.HopMs ?? 0)) - (cells[0].OperationsPerRequest * (first.HopMs ?? 0)));
            rows.Add(new ProofRow(path, label, cells, difference, withoutHops, Verdict(difference, withoutHops, cells, first.Backend.Name, second.Backend.Name)));
        }

        return new ProofResult(
            "done",
            null,
            started,
            DateTimeOffset.UtcNow,
            rounds,
            stores.Select(store => new ProofStore(store.Backend.Key, store.Backend.Name, store.HopMs)).ToArray(),
            rows,
            SentenceFor(rows, stores));
    }

    /// <summary>One path on one store from its samples: failures counted apart, medians and averages over the requests that answered below 400.</summary>
    private static ProofCell Cell(ProofRunner.StoreRun store, string path)
    {
        var all = store.Samples.Where(s => s.Path == path).ToArray();
        var ok = all.Where(s => s.Status < 400).ToArray();
        long[] ms = ok.Select(s => s.Ms).ToArray();
        return new ProofCell(
            store.Backend.Name,
            ok.Length,
            Percentiles.Of(ms, 50),
            Percentiles.Of(ms, 95),
            ok.Length == 0 ? 0 : Math.Round(ok.Average(s => s.Operations), 1),
            store.Backend.Cosmos is null || ok.Length == 0 ? null : Math.Round(ok.Average(s => s.RequestUnits), 2),
            all.Length - ok.Length);
    }

    /// <summary>
    /// The row's phrase: not measured, the same, or which store leads and by
    /// how much, with a note when the lead is all round trip, meaning that
    /// taking one round trip per operation off each side leaves them the same.
    /// </summary>
    private static string Verdict(long? difference, long? withoutHops, ProofCell[] cells, string firstName, string secondName)
    {
        if (difference is null)
        {
            return "not measured";
        }
        long slower = Math.Max(cells[0].P50Ms, cells[1].P50Ms);
        if (Same(difference.Value, slower))
        {
            return "the same";
        }
        string leader = difference < 0 ? secondName : firstName;
        string sentence = $"{leader} leads by {Math.Abs(difference.Value)} ms";
        if (withoutHops is { } adjusted && Same(adjusted, slower))
        {
            sentence += ", all of it the round trip to the store";
        }
        return sentence;
    }

    /// <summary>The one sentence over every row: how many paths came out the same, how many differ only by the round trip, and whether any differ by more.</summary>
    private static string SentenceFor(IReadOnlyList<ProofRow> rows, IReadOnlyList<ProofRunner.StoreRun> stores)
    {
        int same = rows.Count(row => row.Verdict == "the same");
        int measured = rows.Count(row => row.MedianDifferenceMs is not null);
        int hops = rows.Count(row => row.Verdict.EndsWith("the round trip to the store", StringComparison.Ordinal));
        string first = stores[0].Backend.Name;
        string second = stores[1].Backend.Name;
        string trips = string.Join(" and ", stores.Select(store => $"{store.HopMs?.ToString() ?? "?"} ms to {store.Backend.Name}"));
        if (measured == 0)
        {
            return "Nothing could be measured; the rows say why.";
        }
        if (same == measured)
        {
            return $"On every path measured, {first} and {second} answer in the same time, within the tolerance the record states. One round trip to the store is {trips}.";
        }
        if (same + hops == measured)
        {
            return $"On {same} of {measured} paths the two stores answer in the same time. On the other {hops} the difference is the round trip to the store, {trips}, and taking one round trip per operation off each side leaves them the same.";
        }
        return $"On {same} of {measured} paths the two stores answer in the same time; {hops} more differ by exactly the round trip to the store ({trips}); the rest differ by more than that, and the rows say by how much.";
    }
    // #endregion verdict
}
// #endregion proof-result
