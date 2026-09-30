namespace TestProject.Composition;

/// <summary>
/// Sets up the request pipeline: the middleware every request passes through in order, and
/// every response passes back through in reverse. Error handling comes first, so a failure in
/// anything after it becomes a problem document. Then comes the HTTPS redirect for local runs,
/// then the static files for the page. Requests that none of these answer go to the controllers.
/// </summary>
public static class RequestPipeline
{
    /// <summary>Adds the app's middleware in the order it must run.</summary>
    /// <param name="app">The app being built.</param>
    public static void UseTheShedRequestPipeline(this WebApplication app)
    {
        // #region request-pipeline
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        // The HTTPS redirect runs only in Development. In production the hosting platform handles
        // TLS in front of the app, so the app sees every request as plain HTTP. A redirect here
        // would treat those requests as insecure even when the visitor already used HTTPS.
        if (app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }
        app.UseDefaultFiles();
        app.UseStaticFiles();
        // #endregion request-pipeline
    }
}
