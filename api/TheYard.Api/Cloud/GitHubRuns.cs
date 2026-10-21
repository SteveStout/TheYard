// The nightly workflow's runs, read from GitHub's public API once an hour for the Admin tab's
// nightly card (ADR: The Nightly Function on the Admin tab): the last thirty days of runs of the
// one workflow file the Function dispatches, each turned into who started it, which lane it ran,
// how long it took and what it came to. The repository is public, so a token is optional: sixty
// unsigned reads an hour is GitHub's allowance per address, and App Service shares its outbound
// addresses with other sites, so a token set as Nightly:GitHubToken moves the read onto its own
// allowance. None is kept in the repository.
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Api;

// #region github-runs
/// <summary>Reads one workflow's runs from GitHub's public API, and turns each into a <see cref="NightlyRun"/>.</summary>
/// <param name="repo">The repository, owner/name, from configuration (Nightly:Repo).</param>
/// <param name="workflow">The workflow file, from configuration (Nightly:Workflow).</param>
/// <param name="token">A read token, from configuration (Nightly:GitHubToken); null or empty sends none.</param>
public sealed class GitHubRuns(string repo, string workflow, string? token = null)
{
    private static HttpClient Http => OutboundHttp.Client(TimeSpan.FromSeconds(20));

    /// <summary>How the card says who started a run.</summary>
    public const string ByFunction = "Function dispatch", ByHand = "by hand";

    /// <summary>How the card names a run that carries no lane, one older than the workflow's run name.</summary>
    public const string NotNamed = "not named";

    /// <summary>The words a by-hand run's name ends with when it was a dry run.</summary>
    public const string DrySuffix = " (dry run)";

    /// <summary>True when a token is configured, the same test the edge's token passes.</summary>
    public bool Signed => !string.IsNullOrWhiteSpace(token) && !token.StartsWith("__", StringComparison.Ordinal);

    /// <summary>How far back a read asks.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    /// <summary>The address one read asks: the workflow's runs created in the thirty days before <paramref name="now"/>, a hundred at most.</summary>
    /// <param name="now">When the read is made.</param>
    public Uri AddressAt(DateTimeOffset now) =>
        new($"https://api.github.com/repos/{repo}/actions/workflows/{workflow}/runs?per_page=100&created=%3E%3D{(now - Window).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");

    /// <summary>The runs GitHub lists for the thirty days before <paramref name="now"/>; throws what the HTTP call throws.</summary>
    /// <param name="now">When the read is made.</param>
    /// <param name="cancellation">Stops the read.</param>
    public async Task<IReadOnlyList<NightlyRun>> ReadAsync(DateTimeOffset now, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AddressAt(now));
        request.Headers.UserAgent.ParseAdd("TheYard-SelfRead/1 (nightly card)");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (Signed)
        {
            // Unsigned reads share sixty an hour with every site on the same outbound address.
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        using var response = await Http.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        return Parse(answer.RootElement);
    }

    /// <summary>GitHub's list of runs as the card's runs, newest first; empty when the answer holds none.</summary>
    /// <param name="answer">The body GitHub answered with: an object whose workflow_runs is an array.</param>
    public static IReadOnlyList<NightlyRun> Parse(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object || !answer.TryGetProperty("workflow_runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        var parsed = new List<NightlyRun>();
        foreach (var run in runs.EnumerateArray())
        {
            string? id = Whole(run, "id")?.ToString(CultureInfo.InvariantCulture);
            if (id is null || When(run, "created_at") is not { } created)
            {
                continue;
            }
            string trigger = AzureMetrics.Text(run, "event") ?? "";
            string title = AzureMetrics.Text(run, "display_title") ?? "";
            var (lane, dry) = LaneOf(trigger, title);
            string status = AzureMetrics.Text(run, "status") ?? "";
            string? conclusion = AzureMetrics.Text(run, "conclusion");
            var started = When(run, "run_started_at") ?? created;
            long? took = status == "completed" && When(run, "updated_at") is { } updated && updated >= started ? (long)(updated - started).TotalSeconds : null;
            parsed.Add(new NightlyRun(
                id,
                Whole(run, "run_number") ?? 0,
                created,
                NightlySlots.Local(created).ToString("HH:mm", CultureInfo.InvariantCulture),
                trigger == "repository_dispatch" ? ByFunction : ByHand,
                dry,
                lane,
                took,
                conclusion is { Length: > 0 } ? conclusion : status));
        }
        return [.. parsed.OrderByDescending(r => r.At)];
    }

    /// <summary>
    /// The lane a run ran, from its event and its title: a dispatch's title is the dispatch type
    /// ("nightly" is the reply lane, "bookkeeping" the records run), and a by-hand run's title is
    /// the workflow's run name, the mode with " (dry run)" after it when it was one. Anything else
    /// is a run from before the run name, and says so rather than guessing.
    /// </summary>
    /// <param name="trigger">GitHub's event: repository_dispatch or workflow_dispatch.</param>
    /// <param name="title">GitHub's display_title for the run.</param>
    public static (string Lane, bool Dry) LaneOf(string trigger, string title)
    {
        bool dry = trigger == "workflow_dispatch" && title.EndsWith(DrySuffix, StringComparison.Ordinal);
        string name = dry ? title[..^DrySuffix.Length] : title;
        string lane = (trigger, name) switch
        {
            ("repository_dispatch", NightlySlots.Reply) => "reply",
            ("repository_dispatch", NightlySlots.Bookkeeping) => "bookkeeping",
            ("workflow_dispatch", "reply") => "reply",
            ("workflow_dispatch", "bookkeeping") => "bookkeeping",
            _ => NotNamed,
        };
        return (lane, dry && lane != NotNamed);
    }

    /// <summary>A whole number field, read as one: GitHub's run ids pass ten digits, past where a double prints them plainly.</summary>
    private static long? Whole(JsonElement run, string field) =>
        run.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long whole) ? whole : null;

    private static DateTimeOffset? When(JsonElement run, string field) =>
        DateTimeOffset.TryParse(AzureMetrics.Text(run, field), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at) ? at : null;
}
// #endregion github-runs
