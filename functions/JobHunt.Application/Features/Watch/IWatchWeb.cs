using System.Text.Json;

namespace JobHunt.Application.Features.Watch;

/// <summary>
/// The watch's one read of the web: a GET of a public address (the site's card routes and /api/version, GitHub's public
/// API with no token), answered as JSON, or null when it did not answer. It sends nothing and signs in to nothing.
/// </summary>
public interface IWatchWeb
{
    /// <summary>The JSON a public address answered, or null.</summary>
    Task<JsonDocument?> GetAsync(string url, CancellationToken ct);
}
