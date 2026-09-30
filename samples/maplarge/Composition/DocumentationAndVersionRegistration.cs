using TestProject.Library;

namespace TestProject.Composition;

/// <summary>
/// Registers the two services behind the Docs tab and the version in the page footer. This
/// file does not set the version or write any documentation. It only makes the readers
/// available to the controllers:
/// <list type="bullet">
/// <item>The version number is set by the newest entry at the top of docs/CHANGELOG.md.
/// Adding a line there is how a new version is set.</item>
/// <item>The commit is set by the deploy, which puts it in the SHED_COMMIT environment
/// variable of the container. A local run reads it from the .git folder instead.</item>
/// <item>The documentation is the markdown files in the docs folder (the decision records
/// and the guides) plus README.md. DocsController serves them to the Docs tab.</item>
/// </list>
/// </summary>
public static class DocumentationAndVersionRegistration
{
    /// <summary>
    /// Adds DocsCatalog, which lists the documents in sidebar order and finds one by name,
    /// reading them from disk on every request so an edited file shows on the next page
    /// load. Adds VersionInfo, which VersionReader reads once at startup from the changelog
    /// and the commit sources above. Both are singletons rooted at the content root.
    /// </summary>
    /// <param name="builder">The host being built.</param>
    public static void AddTheShedDocumentationAndVersion(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(new DocsCatalog(builder.Environment.ContentRootPath));
        builder.Services.AddSingleton(VersionReader.Read(builder.Environment.ContentRootPath));
    }
}
