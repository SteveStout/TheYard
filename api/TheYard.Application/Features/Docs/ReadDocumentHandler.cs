// A served document, as a query (ADR-115's fourth rung): the catalogue's file by its slug, rendered once a build.
using TheYard.Application.Shared;

namespace TheYard.Application.Features.Docs;

// #region document-handler
/// <summary>The documents the site serves, rendered with their live fences, counts and pictures.</summary>
public interface IDocumentReadings
{
    /// <summary>The rendered markdown of the document with this slug, or null when the catalogue has none.</summary>
    string? Render(string slug);
}

/// <summary>One document by its slug.</summary>
/// <param name="documents">The documents the site serves.</param>
public sealed class ReadDocumentHandler(IDocumentReadings documents) : IQueryHandler<ReadDocumentQuery, Reading>
{
    /// <summary>The markdown, or missing.</summary>
    /// <param name="query">The slug.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<Reading> HandleAsync(ReadDocumentQuery query, CancellationToken cancellation) =>
        Task.FromResult(documents.Render(query.Slug) is { } text
            ? Reading.Of(text)
            : new Reading(ReadingKind.Missing, Title: "No such document", Detail: "No document has that slug."));
}

/// <summary>Asks for one document.</summary>
/// <param name="Slug">The catalogue's slug.</param>
public sealed record ReadDocumentQuery(string Slug);
// #endregion document-handler
