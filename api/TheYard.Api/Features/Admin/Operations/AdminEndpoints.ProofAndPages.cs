// The Admin tab's evidence routes: the page sweep, the gate's test results, the performance proof and the
// partition key experiment. Each asks its one query or command handler (ADR-115's fourth rung); a run that starts
// answers 202 and a busy one 409, in the words each route always used.
using TheYard.Application.Features.Admin.Operations;
using TheYard.Application.Features.Proof;
using TheYard.Application.Shared;

namespace TheYard.Api.Features.Admin.Operations;

/// <summary>The evidence routes of AdminEndpoints; the type and what it is for are described in AdminEndpoints.cs.</summary>
public static partial class AdminEndpoints
{
    /// <summary>Answers every address as the last sweep found it.</summary>
    private static async Task<IResult> PageStatus(ReadPageStatusHandler handler, CancellationToken cancellation) =>
        Answer(await handler.HandleAsync(new ReadPageStatusQuery(), cancellation));

    /// <summary>Starts a sweep of every address: 202 with where to read it, or 409 with why not.</summary>
    private static async Task<IResult> RunPageStatus(RunPageSweepHandler handler, CancellationToken cancellation) =>
        await handler.HandleAsync(new RunPageSweepCommand(), cancellation) is { Kind: ReadingKind.Started }
            ? Results.Accepted("/api/admin/pages")
            : Results.Conflict(new { status = "a sweep is already running, was run in the last twenty seconds, or this container has no address of its own to dial" });

    /// <summary>Answers the gate's counts for this build, or 404 when no results shipped with it.</summary>
    private static async Task<IResult> TestSummaryRead(ReadTestSummaryHandler handler, CancellationToken cancellation) =>
        await handler.HandleAsync(new ReadTestSummaryQuery(), cancellation) is { Kind: ReadingKind.Read } reading
            ? Results.Json(reading.Body, WireFormat)
            : Answer(ReadTestSummaryHandler.NoResults());

    /// <summary>Answers the gate's results file as it shipped, or 404 when none did.</summary>
    private static async Task<IResult> TestResults(ReadTestResultsHandler handler, CancellationToken cancellation) =>
        await handler.HandleAsync(new ReadTestResultsQuery(), cancellation) is { Kind: ReadingKind.Read, Body: string text }
            ? Results.Text(text, "application/json")
            : Answer(ReadTestSummaryHandler.NoResults());

    /// <summary>Answers the proof's status.</summary>
    private static async Task<IResult> ProofStatus(ReadProofStatusHandler handler, CancellationToken cancellation) =>
        Answer(await handler.HandleAsync(new ReadProofStatusQuery(), cancellation));

    /// <summary>Starts the proof: 202 "running", or 409 when it is busy.</summary>
    private static async Task<IResult> ProofStart(int? rounds, StartProofHandler handler, CancellationToken cancellation) =>
        await handler.HandleAsync(new StartProofCommand(rounds ?? ProofRunner.DefaultRounds), cancellation) is { Kind: ReadingKind.Started } started
            ? Results.Json(started.Body, WireFormat, statusCode: StatusCodes.Status202Accepted)
            : Results.Problem(
                detail: "A run is in progress, or the last one finished less than a minute ago. The result is on the card.",
                statusCode: StatusCodes.Status409Conflict,
                title: "The proof is busy");

    /// <summary>Runs the partition key experiment and answers what it measured.</summary>
    private static async Task<IResult> ExperimentRun(RunExperimentHandler handler, CancellationToken cancellation) =>
        Answer(await handler.HandleAsync(new RunExperimentCommand(), cancellation));
}
