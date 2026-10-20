// The relational store's context factories, held where the readings that ask the database itself
// find them: the statement-logging one and the quiet one, both null until the store is attached.
using Microsoft.EntityFrameworkCore;
using TheYard.Infrastructure;

namespace TheYard.Api;

/// <summary>
/// The relational store's two context factories once it is attached, at startup or by the second
/// chance. Held here and not on the backend, so a backend is the same shape for every store and
/// never says which adapter stands behind it (ADR: The onion cut into vertical slices, rule 4).
/// </summary>
public sealed class RelationalContexts
{
    // One reference, written once, so a reader sees both factories or neither.
    private Pair? _held;

    /// <summary>The contexts whose statements reach the Admin tab, or null before the store is attached.</summary>
    public IDbContextFactory<YardDbContext>? Contexts => _held?.Contexts;

    /// <summary>The same database without the statement interceptor, for the readings a minute that must not fill the SQL log with themselves; null before the store is attached.</summary>
    public IDbContextFactory<YardDbContext>? Quiet => _held?.Quiet;

    /// <summary>Holds both factories, once the store is attached.</summary>
    /// <param name="contexts">The statement-logging factory.</param>
    /// <param name="quiet">The quiet factory.</param>
    public void Hold(IDbContextFactory<YardDbContext> contexts, IDbContextFactory<YardDbContext> quiet) => _held = new Pair(contexts, quiet);

    private sealed record Pair(IDbContextFactory<YardDbContext> Contexts, IDbContextFactory<YardDbContext> Quiet);
}
