// The served documents as the document query reads them (ADR-115's fourth rung): the catalogue's file by its
// slug, rendered with its live fences, counts and pictures, and kept once a build (DocumentationEndpoints.Render).
using TheYard.Application.Features.Docs;

namespace TheYard.Api.Features.Docs;

/// <summary>The documents the site serves.</summary>
/// <param name="paths">Where the repository's files are.</param>
/// <param name="build">The build, whose commit keys the kept copy.</param>
public sealed class DocumentReadings(HostPaths paths, BuildInfo build) : IDocumentReadings
{
    /// <inheritdoc/>
    public string? Render(string slug) => DocumentationEndpoints.Render(slug, paths, build);
}
