using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TheYard.Api;

/// <summary>
/// The API's description of itself (ADR: The API describes itself): one
/// OpenAPI document built from the endpoints as they are mapped, a filter that
/// keeps the operator surface out of it, and the two transformers that say
/// what the generator cannot read off a route: what the document is, and
/// which operations need a session.
///
/// <para>The names and routes are constants here so that Program.cs, the
/// reference page and the tests all read the same string, and a test that
/// asks for the document cannot drift from the route that serves it.</para>
/// </summary>
public static class ApiDocument
{
    /// <summary>The one document, and the name in its route.</summary>
    public const string Name = "v1";

    public const string Title = "TheYard API";

    /// <summary>Where the JSON is served: under /api, the one prefix the development server proxies, with the name filled in.</summary>
    public const string DocumentRoute = "/api/openapi/" + Name + ".json";

    /// <summary>Where the reference page is served, by the same container as everything else.</summary>
    public const string ReferenceRoute = "/api/reference";

    /// <summary>Every route under this prefix is an operator surface and stays out of the public document.</summary>
    public const string AdminPrefix = "api/admin";

    /// <summary>The session as a client sends it: a bearer token in the Authorization header.</summary>
    public const string BearerScheme = "Bearer";

    /// <summary>The session as the browser sends it: the same token, in the httpOnly cookie.</summary>
    public const string CookieScheme = "SessionCookie";

    /// <summary>The site's own icon, the same drawing index.html carries inline, so the reference page's tab matches the app's.</summary>
    public const string Favicon = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 32 32'%3E%3Crect width='32' height='32' rx='7' fill='%235e5653'/%3E%3Cpath d='M13 5 8 18h5l-2 9 10-14h-6l4-8z' fill='%23ab978c'/%3E%3C/svg%3E";

    // #region reference-look
    /// <summary>
    /// The reference page on the operator's look, like every other page the site
    /// serves (ADR: The glass look, the addendum on the operator's look): the one
    /// face, IBM Plex Sans, from the same Google stylesheet the diagram pages
    /// use, since no bundle loads here; the site's ground and ink (the ground's
    /// light teal grey behind, white cards, the site's three text colours, the
    /// teal accent), solid rather than the gradient, because the page's own
    /// headers stick while it scrolls and a see-through header would lay words
    /// over words; each of the page's cards on the shared glass with the dark
    /// green rule and two corner brackets, the values repeated from the token
    /// sheet on purpose, as DiagramPage.cs does; and every button a pill, a link
    /// drawn as one included (the live page's Open API Client read square after
    /// 1.0.3.19). The page's code face is Plex too, because the page sets its
    /// paths and methods in it and those are names on the page; a sample keeps
    /// the site's monospaced stack, the one --font-code names, because code reads
    /// like code (ADR: Code that reads like code). The shape rules are marked
    /// important because the page's own sheet sits in cascade layers of its own
    /// and a stylesheet it is given may land in one too.
    /// </summary>
    public const string ReferenceCss = """
        @import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600;700&display=swap');
        body { margin: 0; background: #f3f7f6; font-family: 'IBM Plex Sans', 'Segoe UI', system-ui, Arial, sans-serif; font-variant-numeric: tabular-nums; }
        .scalar-app { --scalar-font: 'IBM Plex Sans', 'Segoe UI', system-ui, Arial, sans-serif; --scalar-font-code: 'IBM Plex Sans', 'Segoe UI', system-ui, Arial, sans-serif; --scalar-color-accent: #006360; --scalar-background-1: #f3f7f6; --scalar-background-2: #ffffff; --scalar-background-3: #e4eeeb; --scalar-border-color: rgba(2, 67, 69, 0.18); --scalar-color-1: #3f3a37; --scalar-color-2: #5e5653; --scalar-color-3: #5f636c; }
        .scalar-app pre, .scalar-app code, .scalar-app [class*="code-block"] { font-family: Consolas, 'SF Mono', Menlo, ui-monospace, monospace !important; }
        .scalar-app button, .scalar-app a[class*="button"] { border-radius: 9999px !important; }
        .scalar-app .scalar-card { position: relative !important; border: 1px solid rgba(2, 67, 69, 0.18) !important; border-top: 3px solid #024345 !important; border-radius: 10px !important; overflow: visible !important; }
        .scalar-app .scalar-card::before, .scalar-app .scalar-card::after { content: '' !important; position: absolute; width: 18px; height: 18px; border-color: #024345; border-style: solid; pointer-events: none; z-index: 1; }
        .scalar-app .scalar-card::before { left: -1px; top: -3px; border-width: 3px 0 0 2px; border-top-left-radius: 10px; }
        .scalar-app .scalar-card::after { right: -1px; bottom: -1px; border-width: 0 2px 2px 0; border-bottom-right-radius: 10px; }
        """;
    // #endregion reference-look

    // #region public-surface
    /// <summary>
    /// Whether an endpoint belongs in the document a stranger reads. One
    /// prefix decides it: the fifteen routes under /api/admin/ are the SQL
    /// ring, the Azure state, the reset-link mint and their kin, and a public
    /// document that indexed them would be disclosure rather than untidiness.
    /// ApiDocumentTests reads the served document and fails if the filter
    /// ever lets one through.
    /// </summary>
    public static bool IsPublic(ApiDescription description) =>
        !(description.RelativePath ?? string.Empty)
            .TrimStart('/')
            .StartsWith(AdminPrefix, StringComparison.OrdinalIgnoreCase);
    // #endregion public-surface

    // #region transformers
    /// <summary>
    /// What the generator cannot read off a route. The document transformer
    /// names the document and declares the two ways a session travels; the
    /// operation transformer marks every endpoint that carries authorization
    /// metadata as requiring one of them, which is the same metadata the
    /// authorization middleware answers 401 from, so the lock in the
    /// reference page and the 401 on the wire cannot disagree.
    /// </summary>
    public static void Configure(OpenApiOptions options, string version, string commit, string? siteUrl)
    {
        options.ShouldInclude = IsPublic;

        options.AddDocumentTransformer((document, _, _) =>
        {
            // The address a client should send requests to. The generator
            // fills this from the request's host, and behind the edge that
            // host is the origin container, which is not an address anybody
            // should be handed (ADR: Accounts and per-user bids, the addendum
            // on the reset link). The site's own configured address when there
            // is one; nothing otherwise, which means "the host you read this
            // from", the right answer for a developer's machine and a test.
            document.Servers = string.IsNullOrWhiteSpace(siteUrl)
                ? []
                : [new OpenApiServer { Url = siteUrl.TrimEnd('/') }];

            document.Info ??= new OpenApiInfo();
            document.Info.Title = Title;
            document.Info.Version = version;
            document.Info.Description =
                "A used-vehicle auction platform in .NET 10: a hundred thousand listings with server-derived "
                + "auction windows, bidding against a simulated room, accounts, and two stores behind one set of "
                + "ports. Every response is snake_case, every instant is milliseconds since the epoch, every "
                + "amount is whole dollars, and every failure is an RFC 9457 problem. Built from commit "
                + commit + ". The decisions behind the API are served beside it, under Decision Records.";

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
            document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "The session token, issued by register, login and reset, sent as a bearer header.",
            };
            document.Components.SecuritySchemes[CookieScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Cookie,
                Name = TokenIssuer.CookieName,
                Description = "The same token as the browser carries it: an httpOnly cookie set by register, login and reset.",
            };
            return Task.CompletedTask;
        });

        // A number is a number. The host's JSON options accept a quoted
        // number in a request body, which is a Web default rather than a
        // choice here, and the generator faithfully writes every integer and
        // double as "integer or string, matching this pattern". Nothing this
        // API answers is ever a quoted number, and a client that sends one is
        // outside the contract, so the document says the type and nothing else.
        options.AddSchemaTransformer((schema, _, _) =>
        {
            if (schema.Type is { } type
                && type.HasFlag(JsonSchemaType.String)
                && (type.HasFlag(JsonSchemaType.Integer) || type.HasFlag(JsonSchemaType.Number)))
            {
                schema.Type = type & ~JsonSchemaType.String;
                schema.Pattern = null;
            }
            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            bool guarded = metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any();
            if (guarded)
            {
                // Two requirements, not one with two schemes: a list is "any
                // of these", and either the header or the cookie opens the door.
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(BearerScheme, context.Document)] = [],
                });
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(CookieScheme, context.Document)] = [],
                });
            }
            return Task.CompletedTask;
        });
    }
    // #endregion transformers
}
