// One read of Cost Management and how it went: the outcome, the reader that asks with the
// site's own identity, and the status the cards read. Its own file because asking Azure
// (tokens, paging, refusals) is a different job from shaping the answer, which is Costs.cs.
using System.Net;
using System.Text.Json;
using TheYard.Application;

namespace TheYard.Api;

// #region cost-read
/// <summary>How a read of Cost Management went: read, refused for want of a role, refused by the billing account, or failed.</summary>
public enum CostOutcome
{
    /// <summary>The read went through and the days are in it.</summary>
    Read,

    /// <summary>The identity holds no role that lets it read costs.</summary>
    NoRole,

    /// <summary>
    /// The service refused for now, as it does while a billing account is set up or when asked to
    /// slow down.
    /// </summary>
    Refused,

    /// <summary>The read did not finish, or there was nothing to ask with.</summary>
    Failed,
}

/// <summary>One hour's read of Cost Management, or why there was none.</summary>
/// <param name="Outcome">How the read went.</param>
/// <param name="Note">Why there is nothing, in words the card can show; null on a clean read.</param>
/// <param name="Days">The charges by resource by day; empty unless the read went through.</param>
/// <param name="Forecast">The forecast days; empty when the forecast could not be had, which does not spoil the actuals.</param>
public sealed record CostRead(CostOutcome Outcome, string? Note, IReadOnlyList<CostDay> Days, IReadOnlyList<CostForecastDay> Forecast);

/// <summary>
/// Reads Cost Management with the site's own identity, the way the telemetry
/// reader reads Application Insights (ADR-024): a token asked of whichever door
/// this host has, for the management endpoint, and no key anywhere. The
/// identity needs Cost Management Reader on the subscription and nothing more.
/// </summary>
public sealed class CostReader(string subscriptionId, string clientId, bool configured)
{
    /// <summary>
    /// The one client every read goes out on. Its limit is a minute, not the usual few
    /// seconds: a first read on a site still busy loading the catalogue can take longer than
    /// twenty seconds, and a shorter limit cancels it (ADR: What Azure charges, addendum).
    /// </summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>What the card says on a run that is not on Azure.</summary>
    public const string NotConfigured = "the cost reader runs only on Azure, where the site has an identity to ask with; a local run reads nothing";

    /// <summary>What the card says when the identity has no role to read costs with.</summary>
    public const string NoRoleNote = "this site's identity does not hold Cost Management Reader on the subscription yet, so Azure will not tell it what it costs";

    /// <summary>True only on Azure, where an identity exists to ask with.</summary>
    public bool Configured => configured && !string.IsNullOrWhiteSpace(subscriptionId);

    /// <summary>The actuals from <paramref name="from"/> to <paramref name="to"/>, and the forecast for the month <paramref name="to"/> falls in.</summary>
    public async Task<CostRead> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellation)
    {
        if (!Configured)
        {
            return new CostRead(CostOutcome.Failed, NotConfigured, [], []);
        }

        string token = await IdentityTokens.AcquireAsync(Http, "https://management.azure.com/", clientId);
        var actuals = await PostAsync(token, "query", CostQuery.Actuals(from, to), cancellation);
        if (actuals.Outcome != CostOutcome.Read)
        {
            return new CostRead(actuals.Outcome, actuals.Note, [], []);
        }

        var days = new List<CostDay>();
        foreach (var page in actuals.Pages)
        {
            days.AddRange(CostQuery.DaysFrom(page));
        }

        // The forecast is a second opinion beside the bill: if it fails, the
        // days still go in and the card says only that there is no forecast.
        // It is asked for the whole month, so its billed days and its days to
        // come add up to the figure the portal shows.
        var monthStart = new DateOnly(to.Year, to.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var forecast = new List<CostForecastDay>();
        var ahead = await PostAsync(token, "forecast", CostQuery.Forecast(monthStart, monthEnd), cancellation);
        if (ahead.Outcome == CostOutcome.Read)
        {
            foreach (var page in ahead.Pages)
            {
                forecast.AddRange(CostQuery.ForecastFrom(page));
            }
        }

        return new CostRead(CostOutcome.Read, null, days, forecast);
    }

    /// <summary>
    /// Posts one question to Cost Management and follows its next-page links, at most five
    /// pages. Says how it went, why not in words a card can show, and the pages it got.
    /// </summary>
    private async Task<(CostOutcome Outcome, string? Note, List<JsonElement> Pages)> PostAsync(string token, string action, object body, CancellationToken cancellation)
    {
        var pages = new List<JsonElement>();
        string? address = $"{CostQuery.Scope(subscriptionId)}/{action}?api-version={CostQuery.ApiVersion}";
        // A month of daily rows by resource is a few hundred rows, one page;
        // the cap is there so a service that keeps answering with a next page
        // cannot hold the recorder.
        for (int page = 0; address is not null && page < 5; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, address) { Content = JsonContent.Create(body) };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var response = await Http.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode)
            {
                return (OutcomeOf(response.StatusCode), NoteFor(response.StatusCode), pages);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            var properties = document.RootElement.GetProperty("properties");
            pages.Add(properties.Clone());
            address = properties.TryGetProperty("nextLink", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
        }

        return (CostOutcome.Read, null, pages);
    }

    /// <summary>Which of the four a status is.</summary>
    public static CostOutcome OutcomeOf(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => CostOutcome.NoRole,
        HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.TooManyRequests => CostOutcome.Refused,
        _ => CostOutcome.Failed,
    };

    /// <summary>
    /// What to say on a public card about a read that did not happen. The
    /// service's own words are not printed: an error from Cost Management names
    /// the scope it refused, and the scope is the subscription's path.
    /// </summary>
    public static string NoteFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => NoRoleNote,
        HttpStatusCode.TooManyRequests => "Cost Management asked this site to slow down; the next read is in an hour",
        HttpStatusCode.BadRequest or HttpStatusCode.Conflict => "Cost Management refused the read, which it does while a billing account is being set up; the next read is in an hour",
        _ => $"Cost Management answered {(int)status}; the next read is in five minutes",
    };
}

/// <summary>What the recorder last found, which is how the card tells "no role" from "nothing kept yet".</summary>
public sealed class CostStatus
{
    /// <summary>The lock that keeps the three values from one read together.</summary>
    private readonly object _gate = new();

    /// <summary>When the last read was tried, or null before the first.</summary>
    public DateTimeOffset? At { get; private set; }

    /// <summary>True when the last read went through.</summary>
    public bool Read { get; private set; }

    /// <summary>Why the last read did not go through, or null when it did.</summary>
    public string? Note { get; private set; }

    /// <summary>Records one read's time, whether it went through, and why not, all under one lock.</summary>
    public void Set(DateTimeOffset at, bool read, string? note)
    {
        lock (_gate)
        {
            At = at;
            Read = read;
            Note = note;
        }
    }

    /// <summary>The three at once, so a reader never sees one read's time beside another's note.</summary>
    public (DateTimeOffset? At, bool Read, string? Note) Snapshot()
    {
        lock (_gate)
        {
            return (At, Read, Note);
        }
    }
}
// #endregion cost-read
