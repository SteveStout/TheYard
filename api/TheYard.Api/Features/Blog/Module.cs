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
    /// <param name="configuration">The host's configuration; the blog reads none.</param>
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
        services.AddScoped(provider => new BlogStatsHandler(provider.GetRequiredService<IBlogStore>(), TimeProvider.System));
        services.AddScoped(provider => new SavePostHandler(provider.GetRequiredService<IBlogStore>(), TimeProvider.System));
        // #endregion blog-wiring
    }

    /// <summary>Maps the blog's routes.</summary>
    /// <param name="app">The built host's routes.</param>
    public void Map(IEndpointRouteBuilder app) => app.MapBlogEndpoints();
}
