using TheYard.Application.Features.Blog;
using TheYard.Infrastructure.Features.Blog;

namespace TheYard.Api.Features.Blog;

/// <summary>
/// The blog (ADR: Every LinkedIn post is also a blog post): where its posts and logs are kept, its
/// handlers and its routes. The posts and both logs live in the relational store whichever site a
/// request arrived on, because the logs join the posts; it is not on the store toggle.
/// </summary>
public sealed class Module : IModule
{
    /// <summary>Registers the store and the handlers.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration: where the pictures are kept.</param>
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // #region blog-wiring
        // The relational store's statement-logging contexts, read at call time: the store attaches at
        // startup or by its second chance, and the blog answers "not attached" until it has.
        services.AddSingleton<IBlogStore>(provider =>
        {
            var held = provider.GetRequiredService<RelationalContexts>();
            return new EfBlogStore(() => held.Contexts);
        });
        services.AddScoped(provider => new ListPostsHandler(provider.GetRequiredService<IBlogStore>(), TimeProvider.System));
        services.AddScoped(provider => new ReadPostHandler(provider.GetRequiredService<IBlogStore>()));
        services.AddScoped(provider => new RecordReadHandler(provider.GetRequiredService<IBlogStore>(), TimeProvider.System));
        services.AddScoped(provider => new RecordEventHandler(provider.GetRequiredService<IBlogStore>(), TimeProvider.System));
        services.AddScoped(provider => new ReadBlogStatsHandler(provider.GetRequiredService<IBlogStore>(), TimeProvider.System));
        services.AddScoped(provider => new SavePostHandler(provider.GetRequiredService<IBlogStore>(), TimeProvider.System));
        // The pictures: the job hunt's storage account's private blog container on Azure, read with the site's
        // identity and kept in the host's memory in front; in this process's memory anywhere else.
        string? account = configuration["Blog:ImagesAccount"], container = configuration["Blog:ImagesContainer"];
        services.AddSingleton<IBlogImageReader>(provider =>
        {
            var identity = provider.GetRequiredService<HostIdentity>();
            IBlogImageReader kept = identity.OnAppService && account is { Length: > 0 } && container is { Length: > 0 }
                ? new BlobBlogImageStore(account, container, _ => IdentityTokens.AcquireAsync(OutboundHttp.Client(TimeSpan.FromSeconds(10)), "https://storage.azure.com/", AzureIds.ClientId(configuration), identity))
                : provider.GetRequiredService<MemoryBlogImageStore>();
            return new CachedBlogImages(kept);
        });
        services.AddSingleton<MemoryBlogImageStore>();
        services.AddScoped(provider => new ReadBlogImageHandler(provider.GetRequiredService<IBlogImageReader>()));
        // #endregion blog-wiring
    }

    /// <summary>Maps the blog's routes.</summary>
    /// <param name="app">The built host's routes.</param>
    public void Map(IEndpointRouteBuilder app) => app.MapBlogEndpoints().MapBlogFeed().MapBlogImages();
}
