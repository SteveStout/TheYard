using Scalar.AspNetCore;

namespace TheYard.Api;

/// <summary>
/// The OpenAPI document and the reference page that renders it, served by this
/// container under /api (ADR: The API describes itself).
/// </summary>
public static class ReferenceEndpoints
{
    /// <summary>Maps /api/openapi/{documentName}.json and /api/reference.</summary>
    public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        // #region api-document-routes
        // The document at /api/openapi/v1.json and the reference page at /api/reference,
        // served by this container like every other document here (ADR: The API
        // describes itself). Under /api rather than at the framework's default
        // /openapi, because /api is the one prefix the development server proxies to
        // this host: at the default address the page's own request for its document
        // came back as index.html on every developer's machine and in the browser
        // suite, and worked only in the container. The page's script comes from the
        // package, not a content delivery network, and the fonts, telemetry, AI chat
        // and MCP link it would reach out for are off; it stays in the one light theme.
        app.MapOpenApi("/api/openapi/{documentName}.json");
        app.MapScalarApiReference(ApiDocument.ReferenceRoute, options => options
            .WithTitle(ApiDocument.Title)
            .WithFavicon(ApiDocument.Favicon)
            .WithOpenApiRoutePattern("/api/openapi/{documentName}.json")
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
            .AddPreferredSecuritySchemes(ApiDocument.BearerScheme)
            .DisableDefaultFonts().DisableTelemetry().DisableAgent().DisableMcp().HideDeveloperTools()
            .ForceLightMode().HideDarkModeToggle().WithCustomCss(ApiDocument.ReferenceCss));
        // #endregion api-document-routes
        return app;
    }
}
