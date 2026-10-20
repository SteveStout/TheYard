namespace TheYard.Api;

/// <summary>
/// Serves the React app for any address that is not an API route or a real file.
/// </summary>
public static class SpaRegistration
{
    /// <summary>Maps the single-page app fallback.</summary>
    public static IEndpointRouteBuilder MapTheYardSpa(this IEndpointRouteBuilder app)
    {
        #region spa-fallback
        // The SPA fallback serves index.html for app routes only; an address that
        // looks like a file (a hashed bundle name that no longer exists, say) is a
        // 404, never a page dressed as a script.
        app.MapFallbackToFile("{*path:nonfile}", "index.html");
        #endregion spa-fallback
        return app;
    }
}
