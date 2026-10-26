// What the Foreman reads (ADR: The Foreman, a place to start): every card under Cloud infrastructure through the
// same reader the Admin tab's cards answer from, so the page and the tab cannot disagree, and the build that answers.
using TheYard.Application.Features.Admin.Cloud;
using TheYard.Application.Features.Admin.Foreman;

namespace TheYard.Api.Features.Admin.Foreman;

// #region foreman-readings
/// <summary>The cards as they read for 24 hours, and the build.</summary>
/// <param name="cards">The Cloud infrastructure cards' reader.</param>
/// <param name="incidents">The failures, through the reader the incidents card reads, so the two cannot disagree.</param>
/// <param name="build">The version this process runs.</param>
/// <param name="start">When this process started.</param>
/// <param name="clock">The clock the page is stamped by.</param>
public sealed class ForemanReadings(ICloudCards cards, IIncidentsReader incidents, BuildInfo build, HostStart start, TimeProvider clock) : IForemanReadings
{
    /// <summary>The cards the Foreman reads, in the rail's order: every card in the one frame.</summary>
    public static readonly IReadOnlyList<(string Key, string Title)> Read =
    [
        ("sqldb", "Azure SQL Database"), ("cosmosdb", "Cosmos DB"), ("registry", "Container Registry"), ("insights", "Application Insights"),
        ("share", "The job-hunt share"), ("blob", "The blob service"), ("mail", "Communication Services"), ("edge", "The Netlify edge"),
        ("nightly", "The Nightly Function"), ("claude", "Claude usage"), ("campaign", "The email campaign"),
    ];

    public async Task<IReadOnlyList<ForemanCard>> CardsAsync(CancellationToken cancellation)
    {
        var read = new List<ForemanCard>();
        foreach (var (key, title) in Read.Where(card => cards.Knows(card.Key)))
        {
            try
            {
                var report = await cards.ReadAsync(key, "24h", cancellation);
                read.Add(new ForemanCard(key, title, report.Tone, report.Headline, report.Note));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A card that fails to read is a gap the page names, never this route's 500.
                read.Add(new ForemanCard(key, title, "unread", null, $"the card did not read ({ex.GetType().Name})"));
            }
        }
        return read;
    }

    public async Task<ForemanFailures> FailuresAsync(CancellationToken cancellation)
    {
        var read = await incidents.FailuresAsync(ReadIncidentsHandler.Window, cancellation);
        return new ForemanFailures(read.Available, read.Note, [.. read.Rows.Select(row => new ForemanFailure(row.Role, row.What, row.Where, row.Count, row.LastMs))]);
    }

    public ForemanBuild Build() => new(build.Version, start.At.ToUnixTimeMilliseconds(), clock.GetUtcNow().ToUnixTimeMilliseconds());
}
// #endregion foreman-readings
