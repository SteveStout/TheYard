// The partition key experiment as the Admin tab asks for it: one port, the shapes of its answer,
// and the answer a container with no document store gives. The experiment itself runs in the
// Cosmos DB adapter (TheYard.Infrastructure.Cosmos/Experiment.cs); the host only asks this port.
namespace TheYard.Application;

/// <summary>One query of the experiment: what it asked, how far it fanned out, and what it cost.</summary>
/// <param name="Query">What the query asked, in words.</param>
/// <param name="Partitions">How far the query fanned out, such as "1 logical" or every physical partition.</param>
/// <param name="RequestCharge">What the query cost, in request units.</param>
/// <param name="DurationMs">How long the query took, in milliseconds.</param>
/// <param name="Documents">How many documents the query returned or counted.</param>
public sealed record ExperimentRow(string Query, string Partitions, double RequestCharge, long DurationMs, int Documents);

/// <summary>The experiment card's answer, including the four ways it can have nothing to show.</summary>
/// <param name="Available">True when the experiment ran and has rows to show.</param>
/// <param name="Reason">Why there is nothing to show, or null when there is.</param>
/// <param name="Container">The container the experiment ran against.</param>
/// <param name="PhysicalPartitions">How many physical partitions the container has.</param>
/// <param name="Documents">How many documents the container holds.</param>
/// <param name="Rows">One row per query the experiment ran.</param>
/// <param name="RanAt">When the experiment ran.</param>
public sealed record ExperimentResult(
    bool Available,
    string? Reason,
    string Container,
    int PhysicalPartitions,
    int Documents,
    IReadOnlyList<ExperimentRow> Rows,
    DateTimeOffset RanAt);

/// <summary>Port: runs the partition key experiment against the store, or says why it cannot.</summary>
public interface IStoreExperiment
{
    /// <summary>The experiment's rows, or an answer that says why there are none.</summary>
    Task<ExperimentResult> RunAsync();
}

/// <summary>The experiment on a container with no document store: nothing to run, and the reason.</summary>
public sealed class NoStoreExperiment : IStoreExperiment
{
    /// <summary>The one instance; it holds nothing.</summary>
    public static readonly NoStoreExperiment Instance = new();

    private NoStoreExperiment()
    {
    }

    /// <summary>An answer with no rows that says this container is not on Azure Cosmos DB.</summary>
    public Task<ExperimentResult> RunAsync() => Task.FromResult(
        new ExperimentResult(false, "this container is not on Azure Cosmos DB", "", 0, 0, [], DateTimeOffset.UnixEpoch));
}
