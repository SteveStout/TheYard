using TestProject.Library;

namespace TestProject.Composition;

/// <summary>
/// The documents the app serves about itself, read from the build at request time, and the
/// version the footer shows (ADR-012).
/// </summary>
public static class DocsRegistration
{
    /// <summary>Registers the catalogue and the version.</summary>
    /// <param name="builder">The host being built.</param>
    public static void AddTheShedDocs(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(new DocsCatalog(builder.Environment.ContentRootPath));
        builder.Services.AddSingleton(VersionReader.Read(builder.Environment.ContentRootPath));
    }
}
