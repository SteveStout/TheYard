using TestProject.Library;

namespace TestProject.Composition;

/// <summary>
/// Registers the services behind the app's own documentation pages and the version in the
/// page footer. The documents are read from the docs folder on each request, and the version
/// and commit are read once here at startup.
/// </summary>
public static class DocsRegistration
{
    /// <summary>
    /// Adds the DocsCatalog, which lists and finds the documents, and the VersionInfo shown in
    /// the footer. The version is the newest line of docs/CHANGELOG.md. The commit comes from the
    /// SHED_COMMIT environment variable when it is set, which is how the deployed container gets
    /// it, and otherwise from the .git folder of a local clone. Both are singletons rooted at the
    /// content root.
    /// </summary>
    /// <param name="builder">The host being built.</param>
    public static void AddTheShedDocs(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(new DocsCatalog(builder.Environment.ContentRootPath));
        builder.Services.AddSingleton(VersionReader.Read(builder.Environment.ContentRootPath));
    }
}
