using System.Text.RegularExpressions;
using System.Xml.Linq;
using TestProject.Documentation;

namespace TestProject.Tests;

/// <summary>
/// Checks what a publish carries. The deployed app reads its documents, and the source files the
/// live code blocks quote, from its published output, not from the source folder. Every other
/// documentation test reads the source folder, so a file the project file leaves out of a publish
/// passes them all and still breaks on the live site: a missing document answers with an error,
/// and a missing quoted file shows "Sample unavailable" in the record. These tests read the
/// Content items in TestProject.csproj and hold every document and every quoted file to them.
/// (more in docs/ADR-012-documents-served-by-the-app.md)
/// </summary>
public sealed class PublishListTests
{
    [Fact]
    public void Every_document_the_catalogue_serves_is_published_with_the_app()
    {
        string root = ProjectFolder.Root();
        List<Regex> published = PublishedPatterns(root);
        var catalogue = new DocumentationCatalog(root);
        List<string> unpublished = catalogue.List()
            .Select(entry => Path.GetRelativePath(root, catalogue.FileFor(entry.Slug)!).Replace('/', '\\'))
            .Where(path => !published.Any(glob => glob.IsMatch(path)))
            .ToList();
        Assert.Empty(unpublished);
    }

    [Fact]
    public void Every_file_a_live_code_block_names_is_published_with_the_app()
    {
        // wwwroot travels with every publish on its own (the Web SDK copies it), so a block that
        // quotes a stylesheet needs no line in the project file. Everything else does.
        string root = ProjectFolder.Root();
        List<Regex> published = PublishedPatterns(root);
        List<string> unpublished = Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md")
            .Append(Path.Combine(root, "README.md"))
            .SelectMany(file => LiveSamples.Fences(File.ReadAllText(file)).Select(fence => (file, fence.Path)))
            .Where(entry => !entry.Path.StartsWith("wwwroot/", StringComparison.Ordinal))
            .Where(entry => !published.Any(glob => glob.IsMatch(entry.Path.Replace('/', '\\'))))
            .Select(entry => $"{Path.GetFileName(entry.file)} quotes {entry.Path}, which a publish leaves out")
            .ToList();
        Assert.Empty(unpublished);
    }

    /// <summary>
    /// The Include patterns of every Content item the project file copies on publish, as regular
    /// expressions over backslash paths, because the project file writes its patterns that way.
    /// </summary>
    private static List<Regex> PublishedPatterns(string root) =>
        XDocument.Load(Path.Combine(root, "TestProject.csproj"))
            .Descendants("Content")
            .Where(item => item.Attribute("CopyToPublishDirectory") is not null && item.Attribute("Include") is not null)
            .SelectMany(item => item.Attribute("Include")!.Value.Split(';'))
            .Select(GlobToRegex)
            .ToList();

    /// <summary>
    /// Turns one MSBuild pattern into a regular expression: "**\" matches any number of folders,
    /// "*" matches within one name, and letter case is ignored the way Windows ignores it.
    /// </summary>
    private static Regex GlobToRegex(string glob) =>
        new("^" + Regex.Escape(glob.Trim()).Replace(@"\*\*\\", @"(.*\\)?", StringComparison.Ordinal).Replace(@"\*", @"[^\\]*", StringComparison.Ordinal) + "$", RegexOptions.IgnoreCase);
}
