using System.Text.RegularExpressions;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// The library on disk reads the way the sidebar reads (ADR: The sidebar, the
/// addendum on the folders). Every document sits in the folder of the sidebar
/// section that offers it, the decision records sit in docs/decisions, and the
/// root of docs/ holds only the folders, the changelog and the pictures.
///
/// <para>Until 1.0.3.84 all of them sat flat in docs/, so a reader on GitHub
/// scrolled one list of a hundred and eighteen files to find what the site
/// already showed in fifteen sections. The folders fix that only while every
/// new document lands in the right one, which nothing but this test sees: the
/// site serves a document by its slug and does not care where the file is.</para>
/// </summary>
public class DocsFolderTests
{
    // #region folders
    /// <summary>
    /// The sidebar section (its name in src/lib/siteMap.ts) to the folder its
    /// documents live in. API Reference and Diagrams hold links and drawings,
    /// no markdown, so they have no folder; the changelog stays in the root
    /// because both deploys read the version from it there.
    /// </summary>
    private static readonly Dictionary<string, string> Folders = new(StringComparer.Ordinal)
    {
        ["about"] = "about",
        ["author"] = "author",
        ["architecture"] = "app-architecture",
        ["stores"] = "sql-vs-cosmos",
        ["performance"] = "performance",
        ["look"] = "style",
        ["traffic"] = "site-traffic",
        ["hosting"] = "hosting",
        ["builtWithAi"] = "built-with-ai",
        ["cicd"] = "ci-cd",
        ["practices"] = "best-practices",
        ["records"] = "decisions",
    };

    /// <summary>What the root of docs/ may hold besides the folders above.</summary>
    private static readonly string[] RootFiles = ["CHANGELOG.md"];

    private static readonly string[] RootFolders = ["images"];
    // #endregion folders

    /// <summary>The section each document's key is offered under, read from sections.ts as source.</summary>
    private static Dictionary<string, string> SectionOfKey()
    {
        string menus = File.ReadAllText(Path.Combine(Repo.Root(), "src", "library", "sections.ts"));
        menus = menus[menus.IndexOf("export const MENUS", StringComparison.Ordinal)..];
        var sectionOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match section in Regex.Matches(
            menus, @"(\w+): \{\s*label: '[^']+',\s*items: \[(.*?)\]", RegexOptions.Singleline))
        {
            foreach (Match key in Regex.Matches(section.Groups[2].Value, @"key: '(\w+)'"))
            {
                sectionOf[key.Groups[1].Value] = section.Groups[1].Value;
            }
        }

        return sectionOf;
    }

    /// <summary>Each document's key to the slug it is served under, read from the two lists in src/library.</summary>
    private static Dictionary<string, string> SlugOfKey()
    {
        string library = Path.Combine(Repo.Root(), "src", "library");
        string lists = File.ReadAllText(Path.Combine(library, "pages.ts"))
            + string.Concat(Directory.EnumerateFiles(Path.Combine(library, "decisionRecords"), "*.ts")
                .Where(path => !path.EndsWith(".test.ts", StringComparison.Ordinal))
                .Select(File.ReadAllText));
        return Regex.Matches(lists, @"(\w+): \{[^{}]*?url: '/api/docs/([a-z0-9-]+)'", RegexOptions.Singleline)
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);
    }

    [Fact]
    public void Every_document_sits_in_the_folder_of_the_section_that_offers_it()
    {
        Dictionary<string, string> sectionOf = SectionOfKey();
        Dictionary<string, string> slugOf = SlugOfKey();
        var wrong = new List<string>();
        int checkedDocuments = 0;

        foreach ((string key, string section) in sectionOf)
        {
            if (!slugOf.TryGetValue(key, out string? slug)
                || !DocumentationCatalog.Files.TryGetValue(slug, out string? path)
                || !path.StartsWith("docs/", StringComparison.Ordinal)
                || RootFiles.Contains(path["docs/".Length..]))
            {
                continue; // the README and the Bicep file are not markdown in docs/, and the changelog stays in its root
            }

            checkedDocuments++;
            string[] parts = path.Split('/');
            if (!Folders.TryGetValue(section, out string? folder))
            {
                wrong.Add($"{path} is offered under '{section}', a section with no folder of its own");
            }
            else if (parts.Length != 3 || parts[1] != folder)
            {
                wrong.Add($"{path} is offered under '{section}' and belongs in docs/{folder}/");
            }
        }

        Assert.True(checkedDocuments > 100, $"only {checkedDocuments} documents were matched to a section, which suggests sections.ts stopped parsing");
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Every_markdown_file_under_docs_is_one_the_catalogue_serves()
    {
        string root = Repo.Root();
        var served = DocumentationCatalog.Files.Values.ToHashSet(StringComparer.Ordinal);
        var unserved = Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !served.Contains(path))
            .ToList();

        Assert.True(unserved.Count == 0, "on disk and served by nothing: " + string.Join(", ", unserved));
    }

    [Fact]
    public void The_root_of_docs_holds_only_the_folders_the_changelog_and_the_pictures()
    {
        string docs = Path.Combine(Repo.Root(), "docs");
        var files = Directory.EnumerateFiles(docs).Select(file => new FileInfo(file).Name).ToList();
        var folders = Directory.EnumerateDirectories(docs).Select(folder => new DirectoryInfo(folder).Name).ToList();

        var strayFiles = files.Where(name => !RootFiles.Contains(name)).ToList();
        var strayFolders = folders
            .Where(name => !RootFolders.Contains(name) && !Folders.ContainsValue(name))
            .ToList();
        var emptyFolders = Folders.Values
            .Where(folder => !Directory.Exists(Path.Combine(docs, folder))
                || !Directory.EnumerateFiles(Path.Combine(docs, folder), "*.md").Any())
            .ToList();

        Assert.True(strayFiles.Count == 0, "files in the root of docs/ that belong in a folder: " + string.Join(", ", strayFiles));
        Assert.True(strayFolders.Count == 0, "folders in docs/ that no sidebar section owns: " + string.Join(", ", strayFolders));
        Assert.True(emptyFolders.Count == 0, "sections whose folder is missing or holds no document: " + string.Join(", ", emptyFolders));
    }
}
