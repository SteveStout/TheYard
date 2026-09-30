namespace TestProject.Composition;

/// <summary>
/// The request pipeline, in ASP.NET Core's own term: the middleware every request passes through
/// in this order, and every response passes back through in reverse. The problem shape comes first,
/// so a failure anywhere after it becomes a problem document; then the local HTTPS redirect; then
/// the page's files. The controllers answer whatever is left.
/// </summary>
public static class RequestPipeline
{
    /// <summary>Adds The Shed's middleware in its required order.</summary>
    /// <param name="app">The app being built.</param>
    public static void UseTheShedRequestPipeline(this WebApplication app)
    {
        // #region request-pipeline
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        // The starter redirected to HTTPS everywhere. Behind the edge that terminates TLS the app
        // only ever sees HTTP, so the redirect stays for the local run and steps aside in production.
        if (app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }
        app.UseDefaultFiles();
        app.UseStaticFiles();
        // #endregion request-pipeline
    }
}
