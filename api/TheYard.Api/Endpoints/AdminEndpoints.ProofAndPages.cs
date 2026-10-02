// The Admin tab's evidence handlers: the page sweep, the gate's test results, the performance
// proof and the partition key experiment. Its own file because each of these reads or starts
// a check of the site rather than a reading of the running process.

using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api;

/// <summary>The proof and page-check handlers of AdminEndpoints; the type and what it is for are described in AdminEndpoints.cs.</summary>
public static partial class AdminEndpoints
{
    /// <summary>Answers the last page sweep's result, whoever asked for it.</summary>
    private static IResult PageStatus(PageStatusRunner pageStatus) =>
        Results.Json(pageStatus.Status);

    /// <summary>
    /// Starts a page sweep now, or answers 409 when one is running, ran in the last twenty seconds, or this
    /// container has no address of its own to dial.
    /// </summary>
    private static IResult RunPageStatus(PageStatusRunner pageStatus) =>
        pageStatus.TryStart("asked")
        ? Results.Accepted("/api/admin/pages")
        : Results.Conflict(new { status = "a sweep is already running, was run in the last twenty seconds, or this container has no address of its own to dial" });

    /// <summary>
    /// Answers the counts from the shipped test results file, without the tests themselves, or a 404 when the build
    /// shipped none.
    /// </summary>
    private static IResult TestSummaryRead(HostPaths paths)
    {
        if (!File.Exists(paths.TestResultsPath))
        {
            return Results.Problem(
                detail: "No test results shipped with this build. The ship's gate writes them.",
                statusCode: StatusCodes.Status404NotFound,
                title: "No test results");
        }

        return Results.Json(TestSummary.Of(paths.TestResultsPath), WireFormat);
    }

    /// <summary>
    /// Answers the shipped test results file as the gate wrote it, or a 404 when the build shipped none.
    /// </summary>
    private static IResult TestResults(HostPaths paths) =>
        File.Exists(paths.TestResultsPath)
        ? Results.Text(File.ReadAllText(paths.TestResultsPath), "application/json")
        : Results.Problem(
            detail: "No test results shipped with this build. The ship's gate writes them.",
            statusCode: StatusCodes.Status404NotFound,
            title: "No test results");

    /// <summary>Answers the performance proof's last result, or the run in progress.</summary>
    private static IResult ProofStatus(ProofRunner proof) =>
        Results.Json(proof.Status);

    /// <summary>
    /// Starts a performance proof of the given rounds (or the default), or answers 409 while a run is on or within
    /// a minute of the last one.
    /// </summary>
    private static IResult ProofStart(int? rounds, ProofRunner proof) =>
        proof.TryStart(rounds ?? ProofRunner.DefaultRounds)
        ? Results.Json(new { status = "running" }, WireFormat, statusCode: StatusCodes.Status202Accepted)
        : Results.Problem(
            detail: "A run is in progress, or the last one finished less than a minute ago. The result is on the card.",
            statusCode: StatusCodes.Status409Conflict,
            title: "The proof is busy");

    /// <summary>
    /// Answers the partition key experiment's queries with the request charge beside each, or an empty answer on a
    /// container with no document store.
    /// </summary>
    private static async Task<IResult> ExperimentRun(IServiceProvider services)
    {
        var cosmos = services.GetService<CosmosStore>();
        return cosmos is null
            ? Results.Json(new { available = false, reason = "this container is not on Azure Cosmos DB", rows = Array.Empty<object>() })
            : Results.Json(await Experiment.RunAsync(cosmos));
    }
}
