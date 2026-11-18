using System.Text.Json;
using TheYard.Application.Features.Blog;
using TheYard.Data;

namespace TheYard.Api.Features.Blog;

/// <summary>
/// The blog's routes (ADR: Every LinkedIn post is also a blog post): the posts and one post for the page,
/// the page's report of a read and of what the reader did, the Admin tab's Blog analytics card, and the
/// operator's one write path. A read is kept under the day's keyed hash; no address and no user agent is
/// stored, the agent is read for a device class and a bot guess and dropped.
/// </summary>
public static class BlogEndpoints
{
    private static readonly JsonSerializerOptions WireFormat = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>Maps the blog's six routes.</summary>
    public static IEndpointRouteBuilder MapBlogEndpoints(this IEndpointRouteBuilder app)
    {
        // #region blog-endpoints
        app.MapGet("/api/blog/posts", Posts)
            .WithName("GetBlogPosts").WithTags("Blog")
            .WithSummary("A page of cards for a filter (q, topic, keyword, month, year, sort, after), with the total, the chips' counts and the next cursor")
            .Produces<BlogWire.List>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapGet("/api/blog/posts/{slug}", Post)
            .WithName("GetBlogPost").WithTags("Blog")
            .WithSummary("One published post by its slug: its body drawn, its counts, its older, newer and related posts")
            .Produces<BlogWire.Post>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/blog/reads", Read)
            .WithName("RecordBlogRead").WithTags("Blog")
            .WithSummary("The page's report that a post was opened: where from, never who")
            .Produces<SourceWire>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/blog/events", Event)
            .WithName("RecordBlogEvent").WithTags("Blog")
            .WithSummary("What a reader did on a post: depth, time, see more, a slide, a click, a copy")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // Public like the rest of the Admin tab: counts and words, never a reader.
        app.MapGet("/api/admin/blog", Stats)
            .WithName("GetBlogStats").WithTags("Admin")
            .WithSummary("Each post's reads, readers, depth, time and clicks, and the month's reads by source")
            .Produces(StatusCodes.Status200OK);

        app.MapPost("/api/admin/blog/posts", Save)
            .WithName("SaveBlogPost").WithTags("Admin")
            .WithSummary("Adds a post or replaces the one with its slug; the operator's key only")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        // #endregion blog-endpoints
        return app;
    }

    /// <summary>Where a kept read came from.</summary>
    /// <param name="Source">linkedin, ref, search or direct.</param>
    public sealed record SourceWire(string Source);

    /// <summary>
    /// The filter as the address carries it: each part kept only in the shape it can have, and anything else left out
    /// rather than refused, so a shared link with a stray part still opens the blog.
    /// </summary>
    /// <param name="query">The request's query string.</param>
    public static BlogFilter FilterOf(IQueryCollection query)
    {
        static string? Part(IQueryCollection query, string name, int longest) =>
            query[name].ToString().Trim() is { Length: > 0 } value && value.Length <= longest ? value : null;
        static string? Key(string? value) => value is not null && value.All(c => c is (>= 'a' and <= 'z') or '-') ? value : null;
        string? month = Part(query, "month", 7);
        return new BlogFilter(
            Part(query, "q", 100),
            Key(Part(query, "topic", 40)),
            Key(Part(query, "keyword", 40)),
            month is not null && Domain.Features.Blog.BlogListRules.MonthRange(month) is not null ? month : null,
            int.TryParse(Part(query, "year", 4), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int year) ? year : null,
            Part(query, "sort", 8) == "read" ? "read" : "new",
            Part(query, "after", 80));
    }

    private static async Task<IResult> Posts(HttpContext http, ListPostsHandler handler, BlogAnswers answers, CancellationToken cancellation)
    {
        var filter = FilterOf(http.Request.Query);
        string? unavailable = null;
        var answer = await answers.GetAsync($"list|{filter}", async token =>
        {
            var (page, reason) = await handler.HandleAsync(new ListPostsQuery(filter), token);
            unavailable = reason;
            return page is null ? null : JsonSerializer.SerializeToUtf8Bytes(BlogWire.Of(page), WireFormat);
        }, cancellation);
        return answer is not null
            ? BlogAnswers.Send(http, answer)
            : Results.Problem(detail: unavailable, statusCode: 503, title: "The blog cannot be read now");
    }

    private static async Task<IResult> Post(string slug, HttpContext http, ReadPostHandler handler, BlogAnswers answers, CancellationToken cancellation)
    {
        var answer = await answers.GetAsync($"post|{slug}", async token =>
            await handler.HandleAsync(new ReadPostQuery(slug), token) is { } page ? JsonSerializer.SerializeToUtf8Bytes(BlogWire.Of(page), WireFormat) : null, cancellation);
        return answer is not null
            ? BlogAnswers.Send(http, answer)
            : Results.Problem(detail: $"No published post has the slug {slug}.", statusCode: 404, title: "No such post");
    }

    /// <summary>What the page sends when a post opens: its slug, the referring host, and the address's code and utm fields.</summary>
    /// <param name="Slug">The post.</param>
    /// <param name="Referrer">The referring page; only its host is kept.</param>
    /// <param name="Ref">The tagged link's code, from ?r=.</param>
    /// <param name="UtmSource">The address's utm_source.</param>
    /// <param name="UtmMedium">The address's utm_medium.</param>
    /// <param name="UtmCampaign">The address's utm_campaign.</param>
    public sealed record ReadWire(string? Slug, string? Referrer, string? Ref, string? UtmSource, string? UtmMedium, string? UtmCampaign);

    /// <summary>What the page sends when the reader does something.</summary>
    /// <param name="Slug">The post.</param>
    /// <param name="Type">depth, time, more, slide, click or copy.</param>
    /// <param name="Value">What the event carries.</param>
    public sealed record EventWire(string? Slug, string? Type, string? Value);

    private static async Task<IResult> Read(HttpContext http, VisitorTokens tokens, RecordReadHandler handler, CancellationToken cancellation)
    {
        var wire = await Body<ReadWire>(http, cancellation);
        if (wire?.Slug is not { Length: > 0 } slug)
        {
            return Results.Problem(detail: "The body is the post's slug and where the reader came from, as JSON.", statusCode: 400, title: "The read could not be kept");
        }
        string? code = wire.Ref is { Length: 6 } r && r.All(char.IsAsciiLetterOrDigit) ? r.ToLowerInvariant() : null;
        var (session, bot, device) = Reader(http, tokens);
        string? source = await handler.HandleAsync(new RecordReadCommand(slug, Host(wire.Referrer), code, wire.UtmSource, wire.UtmMedium, wire.UtmCampaign, device, Country(http), session, bot), cancellation);
        return source is null
            ? Results.Problem(detail: $"No published post has the slug {slug}.", statusCode: 404, title: "No such post")
            : Results.Json(new SourceWire(source), WireFormat);
    }

    private static async Task<IResult> Event(HttpContext http, VisitorTokens tokens, RecordEventHandler handler, CancellationToken cancellation)
    {
        var wire = await Body<EventWire>(http, cancellation);
        if (wire?.Slug is not { Length: > 0 } slug || wire.Type is not { Length: > 0 } type)
        {
            return Results.Problem(detail: "The body is the post's slug, the event's type and its value, as JSON.", statusCode: 400, title: "The event could not be kept");
        }
        var (session, bot, _) = Reader(http, tokens);
        return await handler.HandleAsync(new RecordEventCommand(slug, type, wire.Value ?? "", session, bot), cancellation) is { } wrong
            ? Results.Problem(detail: wrong, statusCode: 400, title: "The event could not be kept")
            : Results.NoContent();
    }

    private static async Task<IResult> Stats(ReadBlogStatsHandler handler, CancellationToken cancellation)
    {
        var (stats, sources, kept, pictures) = await handler.HandleAsync(new ReadBlogStatsQuery(), cancellation);
        return Results.Json(new { kept = kept.Reason, available = kept.Available, posts = stats, sources, pictures }, WireFormat);
    }

    /// <summary>A post as the operator sends it.</summary>
    /// <param name="Slug">Its address on the blog.</param>
    /// <param name="Title">Its first line.</param>
    /// <param name="Summary">Two lines for a short card.</param>
    /// <param name="Body">The text as LinkedIn has it.</param>
    /// <param name="PublishedAt">When LinkedIn first had it, read on LinkedIn.</param>
    /// <param name="LinkedinUrl">The post on LinkedIn, read on LinkedIn.</param>
    /// <param name="CodeUrl">The code it talks about.</param>
    /// <param name="Images">Its slides.</param>
    /// <param name="Tags">Its hashtags.</param>
    public sealed record SaveWire(string? Slug, string? Title, string? Summary, string? Body, DateTimeOffset? PublishedAt, string? LinkedinUrl, string? CodeUrl, IReadOnlyList<BlogImage>? Images, IReadOnlyList<string>? Tags);

    private static async Task<IResult> Save(HttpContext http, AdminKey adminKey, SavePostHandler handler, BlogAnswers answers, CancellationToken cancellation)
    {
        if (!adminKey.IsPresentedBy(http.Request))
        {
            return Results.Problem(detail: $"Send the operator's key in {AdminKey.HeaderName}.", statusCode: 401, title: "The key is missing or wrong");
        }
        var wire = await Body<SaveWire>(http, cancellation);
        if (wire?.PublishedAt is not { } published)
        {
            return Results.Problem(detail: "The body is one post as JSON, with published_at an ISO 8601 instant.", statusCode: 400, title: "The post could not be read");
        }
        var post = new BlogPost(0, wire.Slug ?? "", wire.Title ?? "", wire.Summary ?? "", wire.Body ?? "", published, wire.LinkedinUrl ?? "", wire.CodeUrl, wire.Images ?? [], wire.Tags ?? []);
        var (id, wrong, unavailable) = await handler.HandleAsync(new SavePostCommand(post), cancellation);
        if (id is not null)
        {
            answers.Clear();
        }
        return id is { } kept ? Results.Json(new { id = kept, slug = post.Slug }, WireFormat)
            : unavailable is { } reason ? Results.Problem(detail: reason, statusCode: 503, title: "The blog cannot keep posts now")
            : Results.Problem(detail: wrong, statusCode: 400, title: "The post could not be kept");
    }

    /// <summary>The reader as the logs keep them: the day's keyed hash, the bot guess, the device class; the address and the agent go no further.</summary>
    private static (byte[] Session, bool Bot, string Device) Reader(HttpContext http, VisitorTokens tokens)
    {
        string? agent = http.Request.Headers.UserAgent.ToString();
        bool bot = Hits.LooksLikeABot(agent, http.Request.Path) || agent.Contains(Hits.SelfTag, StringComparison.Ordinal);
        return (tokens.BlogSessionFor(VisitorTokens.AddressOf(http), DateTimeOffset.UtcNow), bot, BlogRules.Device(agent, bot));
    }

    /// <summary>The referring page's host, lower case; null for none or for an address that is not one.</summary>
    private static string? Host(string? referrer) =>
        Uri.TryCreate(referrer, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.Host.ToLowerInvariant() : null;

    /// <summary>The edge's two-letter country when it sends one; never worked out from the address.</summary>
    private static string? Country(HttpContext http) =>
        http.Request.Headers["X-Country"].ToString() is { Length: 2 } c && c.All(char.IsAsciiLetter) ? c.ToUpperInvariant() : null;

    private static async Task<T?> Body<T>(HttpContext http, CancellationToken cancellation)
        where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(http.Request.Body, WireFormat, cancellation);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
