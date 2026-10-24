// The performance proof as a query and a command (ADR-115's fourth rung): its status, and a run started.
using TheYard.Application.Shared;

namespace TheYard.Application.Features.Proof;

// #region proof-handlers
/// <summary>The proof as it stands: the last run's rounds, or the one going.</summary>
public interface IProofReadings
{
    /// <summary>The status the card draws.</summary>
    object Status();
}

/// <summary>Starts the proof.</summary>
public interface IProofRuns
{
    /// <summary>Starts a run of this many rounds; false when one is going or finished less than a minute ago.</summary>
    bool Start(int rounds);
}

/// <summary>The proof's status.</summary>
/// <param name="proof">The proof as it stands.</param>
public sealed class ReadProofStatusHandler(IProofReadings proof) : IQueryHandler<ReadProofStatusQuery, Reading>
{
    /// <summary>The status.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<Reading> HandleAsync(ReadProofStatusQuery query, CancellationToken cancellation) => Task.FromResult(Reading.Of(proof.Status()));
}

/// <summary>Asks for the proof's status.</summary>
public sealed record ReadProofStatusQuery;

/// <summary>Starts the proof, or says it is busy.</summary>
/// <param name="runs">Starts the proof.</param>
public sealed class StartProofHandler(IProofRuns runs) : ICommandHandler<StartProofCommand, Reading>
{
    /// <summary>Started with "running", or busy.</summary>
    /// <param name="command">How many rounds.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<Reading> HandleAsync(StartProofCommand command, CancellationToken cancellation) =>
        Task.FromResult(runs.Start(command.Rounds)
            ? new Reading(ReadingKind.Started, new ProofStarted("running"))
            : new Reading(ReadingKind.Busy, Title: "The proof is busy", Detail: "A run is in progress, or the last one finished less than a minute ago. The result is on the card."));
}

/// <summary>Starts the proof.</summary>
/// <param name="Rounds">How many rounds the run makes.</param>
public sealed record StartProofCommand(int Rounds);

/// <summary>What a started run answers.</summary>
/// <param name="Status">Always "running".</param>
public sealed record ProofStarted(string Status);
// #endregion proof-handlers
