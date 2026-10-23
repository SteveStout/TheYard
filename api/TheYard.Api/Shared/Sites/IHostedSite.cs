namespace TheYard.Api.Shared.Sites;

// #region hosted-site
/// <summary>
/// A whole application this process hosts under a name of its own, beside the Yard's sites (ADR:
/// The onion cut into vertical slices, rule 5). The host starts it once it is built, hands it every
/// request that arrived on its name before anything of TheYard's pipeline sees the request, and
/// disposes it after the server has drained. The Shed is one; a second is one more implementation
/// and one more name in the site list.
/// </summary>
public interface IHostedSite : IAsyncDisposable
{
    /// <summary>The public name the site answers to, lower case.</summary>
    string Host { get; }

    /// <summary>True once the site has started and until it is disposed.</summary>
    bool Running { get; }

    /// <summary>Starts the site; throws when it cannot, and its name then answers 503.</summary>
    /// <param name="stopping">Cancelled when the host begins to stop.</param>
    Task StartAsync(CancellationToken stopping);

    /// <summary>Answers one request that arrived on the site's name.</summary>
    /// <param name="context">The request.</param>
    Task HandleAsync(HttpContext context);
}
// #endregion hosted-site
