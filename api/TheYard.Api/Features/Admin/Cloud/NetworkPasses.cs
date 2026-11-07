// The network log's newest pass, for the card's logging row (ADR-124): the Function that runs it, by its resource
// id for the portal link, and the newest run its history lists, read through the same port the run history route
// reads, so the row says what the pass came to and not only what the switch says.
using System.Globalization;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The Function that runs the network pass, and its newest listed run as the hour's facts.</summary>
/// <param name="function">The Function app's resource id, for its portal link on the card; null when it is not known.</param>
/// <param name="runs">The Function's run history; null where it is not read.</param>
public sealed class NetworkPasses(string? function, IProcessRunsReader? runs)
{
    /// <summary>Neither the Function nor its history: the card says the pass was not read.</summary>
    public static readonly NetworkPasses None = new(null, null);

    /// <summary>How far back the newest pass is looked for: far enough to name the last one after a silent day.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    /// <summary>The Function app's resource id.</summary>
    public string? Function => function;

    /// <summary>The newest listed pass as facts: what it came to, when, and its words; or why it was not read.</summary>
    /// <param name="cancellation">Stops the read.</param>
    public async Task<IReadOnlyDictionary<string, string?>> PassAsync(CancellationToken cancellation)
    {
        var facts = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (runs is null || FunctionProcesses.Find(NetworkCard.PassProcess) is not { } process)
        {
            facts[NetworkCard.PassNoteFact] = "the Function's run history is not read here";
            return facts;
        }
        try
        {
            var read = await runs.RunsAsync(process, Window, 0, 1, cancellation);
            if (!read.Available)
            {
                facts[NetworkCard.PassNoteFact] = read.Note ?? "the Function's run history did not answer";
                return facts;
            }
            facts[NetworkCard.PassNoteFact] = null;
            if (read.Rows is [var run, ..])
            {
                facts[NetworkCard.PassFact] = run.Outcome;
                facts[NetworkCard.PassAtFact] = run.At.ToString("O", CultureInfo.InvariantCulture);
                facts[NetworkCard.PassCameFact] = ProcessWords.Came(run);
            }
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested)
        {
            facts[NetworkCard.PassNoteFact] = $"the Function's run history failed ({ex.GetType().Name})";
        }
        return facts;
    }
}
