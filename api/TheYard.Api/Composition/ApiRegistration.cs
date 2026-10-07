using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;

namespace TheYard.Api;

/// <summary>
/// The shape of the wire: snake_case bodies, one ProblemDetails for every failure
/// (ADR-023), the edge's forwarded headers, the catalogue's reads compressed,
/// and the OpenAPI document built from the endpoints as mapped (ADR: The API
/// describes itself).
/// </summary>
public static class ApiRegistration
{
    /// <summary>Registers the JSON options, the forwarded headers, the problem shape, compression and the OpenAPI document.</summary>
    public static void AddTheYardApi(this WebApplicationBuilder builder, YardComposition host)
    {
        string buildVersion = host.Build.Version;
        string buildCommit = host.Build.Commit;
        string? siteUrl = host.SiteUrl;
        // No InventoryService, BidService or MarketService in the container. Each
        // backend owns its own three (the room too: one room per store, held in
        // memory for the life of the container, ADR-027), and an endpoint reaches
        // them through CurrentBackend, which is scoped to the request that chose the
        // store. A singleton of any of the three would be a singleton of one store.

        // Request bodies are snake_case like everything else on this wire.
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);

        #region forwarded-headers
        // Every visitor reaches this process through two proxies: the edge (Netlify,
        // which proxies each site's domain to its origin) and App Service's own front
        // end, which terminates TLS and speaks plain HTTP to the container. Each adds
        // the address it received the request from to the right of X-Forwarded-For,
        // and says the scheme in X-Forwarded-Proto. So the visitor is the second entry
        // from the right, and anything to the left of it is whatever the visitor sent,
        // which is never read.
        //
        // Neither proxy has an address this app can list (the edge's are not fixed,
        // and App Service's front end is a pool), so the trust is by count, not by
        // address: KnownProxies and KnownIPNetworks are emptied, which means "any
        // sender", and ForwardLimit is the number of proxies, two. The count is a
        // setting, Edge:ForwardLimit, because the day the edge changes the number of
        // hops changes, and /api/admin/arrival shows the raw headers beside what this
        // resolved, so a wrong count is read live rather than guessed. A caller that
        // skips the edge and calls the origin directly can still write the second
        // entry; what that buys them is a different visitor count, because the
        // address feeds counts and never a permission (ADR: The order of the request
        // pipeline). X-Forwarded-Host is not read at all: the site's own address is
        // configuration (Site:Url), never a header.
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = builder.Configuration.GetValue("Edge:ForwardLimit", 2);
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
        });
        #endregion forwarded-headers

        #region problem-details
        // Every failure answers RFC 9457 ProblemDetails (ADR-023): one shape for a
        // rejected query, a rejected bid and an unhandled exception alike, so a caller
        // reads one field, `detail`, for the message. The trace identifier ties the
        // response to the request's log line.
        builder.Services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
        // What goes in that shape when nothing planned the failure: which exceptions
        // are the caller's fault and may say so, what a 500 is allowed to reveal, and
        // the log line that makes the returned trace id worth having (ADR-030).
        builder.Services.AddExceptionHandler<ProblemHandler>();
        // A body that will not parse answers a bare 400 with nothing in it by default,
        // because the framework would rather not spend an exception on a bad request.
        // That left one kind of failure on this API with no sentence in it. One shape
        // for every failure is worth an exception on a request that was already wrong
        // (ADR-030).
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

        // No HTTP logging middleware. AddHttpLogging and UseHttpLogging stood here from
        // the first commit to the pipeline lane, and never wrote a line: the logging
        // configuration has always held Microsoft.AspNetCore at Warning, and the
        // middleware logs at Information, so it checked, found itself filtered, and
        // passed every request on. Every request is already recorded twice, by the
        // timing ring in RequestPipeline.cs and by OpenTelemetry's request telemetry to
        // Application Insights, whose daily cap a line per request would spend by noon
        // (ADR: The order of the request pipeline).
        builder.Logging.AddJsonConsole(options => options.IncludeScopes = false);
        #endregion problem-details

        #region compression
        // What: the listing and the filter values leave the container compressed, as
        // Brotli or gzip, whichever the caller asks for. Nothing else is compressed.
        //
        // Why: every API read goes browser to edge to Azure and back. The edge always
        // compressed the answer for the browser, but it asked Azure for it plainly, so a
        // page of a hundred vehicles crossed from Azure to the edge as about 106 KB of
        // JSON. Compressed here it crosses as about 15 KB.
        //
        // How: this registers the compressor; RequestPipeline switches it on for the
        // two addresses CatalogueReads names and no others. Timed on the build machine,
        // a page took 79 to 100 ms plain and 87 to 96 ms as Brotli, so the cost is lost
        // in the noise of building the page.
        builder.Services.AddResponseCompression(options =>
        {
            // HTTPS compression is off by default because of the BREACH attack. It is
            // safe here because only the two catalogue reads reach it and their bodies
            // hold no secret; RequestPipeline explains the rule.
            options.EnableForHttps = true;
            options.MimeTypes = ["application/json"];
            // Listed in order of preference: a caller that accepts both gets Brotli,
            // the smaller of the two.
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });
        // Optimal, which for Brotli is quality 4, and not Fastest. The serializer
        // writes the answer in many small pieces and each one is flushed through the
        // compressor; at the fastest level each flush throws away most of the gain, and
        // a page came out at 44 KB instead of about 15 KB.
        builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal);
        builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal);
        #endregion compression

        #region api-document
        // The API's description of itself (ADR: The API describes itself): one
        // document built from the endpoints below as they are mapped, the operator
        // surface filtered out of it, and the two schemes a session travels by
        // declared. The version is the build's, because that is the honest number.
        builder.Services.AddOpenApi(ApiDocument.Name, options => ApiDocument.Configure(options, buildVersion, buildCommit, siteUrl));
        #endregion api-document
    }
}
