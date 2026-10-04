// A relational context per call, from options fixed at startup. It lives with the other
// relational adapters because it builds YardDbContext, which nothing outside this ring should
// construct; the host only asks it for a context.
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace TheYard.Infrastructure;

/// <summary>
/// A context per call, from options fixed at startup. The relational backend
/// is built before the host's service container is, so it cannot take the factory the
/// container would have registered; this is the same thing with no pool and
/// no service provider behind it.
///
/// <para>One thing the container's factory did for free has to be done by
/// hand: Entity Framework logs every command through the application's logger
/// factory, which is what puts its `@p='?'` lines in the Admin tab's log
/// section, and that factory does not exist until the application is built.
/// <see cref="Attach"/> hands it over then, before the catalogue is read, so
/// the first statement is logged like the last.</para>
/// </summary>
public sealed class ContextFactory(DbContextOptions<YardDbContext> options) : IDbContextFactory<YardDbContext>
{
    /// <summary>The options every context is built from; replaced once when the loggers arrive.</summary>
    private DbContextOptions<YardDbContext> _options = options;

    /// <summary>A new context for one operation; the caller disposes it.</summary>
    public YardDbContext CreateDbContext() => new(_options);

    /// <summary>The application's logging, once there is an application.</summary>
    public void Attach(ILoggerFactory loggers) =>
        _options = new DbContextOptionsBuilder<YardDbContext>(_options).UseLoggerFactory(loggers).Options;
}
