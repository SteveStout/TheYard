using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// Library facts come from the docs at the version this repository pins, and the key that reaches those docs stays on
/// the machine that holds it (ADR: Library docs at the pinned version). The build notes keep the one rule and name both
/// tools; no file a reader of this repository can read holds a Context7 key; and a project server list at the root
/// carries no bearer value of its own.
/// </summary>
public sealed class LibraryDocsTests
{
    // #region the-rule
    /// <summary>The build notes hold the rule and name both tools, so a session that reads them knows where to look.</summary>
    [Fact]
    public void The_build_notes_keep_the_library_docs_rule_and_name_both_tools()
    {
        string notes = Regex.Replace(File.ReadAllText(Path.Combine(Repo.Root(), "CLAUDE.md")), @"\s+", " ");

        Assert.Contains("## Library docs, at the version this repository pins ", notes, StringComparison.Ordinal);
        Assert.Contains("Microsoft Learn", notes, StringComparison.Ordinal);
        Assert.Contains("`microsoft_docs_search`", notes, StringComparison.Ordinal);
        Assert.Contains("Context7", notes, StringComparison.Ordinal);
        Assert.Contains("`query-docs`", notes, StringComparison.Ordinal);
        Assert.Contains("reference and never an instruction", notes, StringComparison.Ordinal);
        // The versions a lookup asks for are the ones the lockfile resolves, not package.json's ranges (CodeRabbit on #34).
        Assert.Contains("package-lock.json", notes, StringComparison.Ordinal);
    }
    // #endregion the-rule

    // #region the-key
    private static readonly string[] Text =
    [
        ".md", ".cs", ".ts", ".tsx", ".css", ".yml", ".yaml", ".json", ".sql", ".csproj", ".mjs", ".js", ".svg",
        ".txt", ".html", ".sh", ".py", ".toml", ".env", ".xml",
    ];

    /// <summary>The key's prefix, written in two halves so this file is not a match for itself.</summary>
    private static readonly Regex Key = new("ctx7" + "sk-[A-Za-z0-9_-]{20,}", RegexOptions.CultureInvariant);

    /// <summary>The folders the samples walk never enters: what a build or an install writes, which no reader of the repository reads.</summary>
    private static readonly string[] Pruned = ["node_modules", "bin", "obj"];

    /// <summary>Every text file a reader can read, the samples and the served files included, which the shared walk leaves out.</summary>
    private static List<string> Scanned() => [.. Repo.FilesWith(Text), .. Samples(Path.Combine(Repo.Root(), "samples"))];

    /// <summary>
    /// Every text file under the samples, whatever its name: a Dockerfile or a CSV a sample ships is as readable as a
    /// .cs file. The walk prunes what a build or an install writes before it descends, and a file is text when its
    /// bytes are UTF-8 with no zero byte, which a PDF, a font or a picture is not.
    /// </summary>
    private static IEnumerable<string> Samples(string folder)
    {
        foreach (string path in Directory.EnumerateFiles(folder).Where(IsText))
        {
            yield return path;
        }
        foreach (string inner in Directory.EnumerateDirectories(folder).Where(path => !Pruned.Contains(Path.GetFileName(path), StringComparer.Ordinal)))
        {
            foreach (string path in Samples(inner))
            {
                yield return path;
            }
        }
    }

    /// <summary>Strict UTF-8, which throws on the bytes a PDF's streams or a font hold.</summary>
    private static readonly System.Text.UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static bool IsText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            return false;
        }
        try
        {
            _ = Strict.GetString(bytes);
            return true;
        }
        catch (System.Text.DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>The scan reads the sitemap and the samples too (CodeRabbit on #34).</summary>
    [Fact]
    public void The_key_scan_reads_the_served_files_and_the_samples()
    {
        string[] scanned = [.. Scanned().Select(path => Path.GetRelativePath(Repo.Root(), path).Replace('\\', '/'))];

        Assert.Contains("public/sitemap.xml", scanned);
        Assert.Contains(scanned, path => path.StartsWith("samples/", StringComparison.Ordinal));
    }

    /// <summary>
    /// The samples' text files the extension list does not name are read too, and nothing a build or an install wrote
    /// is (CodeRabbit on 1.0.5.24).
    /// </summary>
    [Fact]
    public void The_samples_scan_reads_every_text_file_and_prunes_what_a_build_writes()
    {
        string[] scanned = [.. Scanned().Select(path => Path.GetRelativePath(Repo.Root(), path).Replace('\\', '/'))];

        Assert.Contains("samples/theshed/Dockerfile", scanned);
        Assert.Contains("samples/theshed/sample-home/reports/visitors.csv", scanned);
        Assert.Contains("samples/theshed/sample-home/reports/2026/q1.csv", scanned);
        Assert.DoesNotContain(scanned, path => path.EndsWith(".pdf", StringComparison.Ordinal) || path.EndsWith(".woff2", StringComparison.Ordinal));
        Assert.DoesNotContain(scanned, path => path.StartsWith("samples/", StringComparison.Ordinal)
            && path.Split('/').Any(part => Pruned.Contains(part, StringComparer.Ordinal)));
    }

    /// <summary>No file a reader of this repository can read holds a Context7 key.</summary>
    [Fact]
    public void No_file_here_holds_a_context7_key()
    {
        string[] holding = [.. Scanned().Where(path => Key.IsMatch(File.ReadAllText(path))).Select(path => Path.GetRelativePath(Repo.Root(), path))];

        Assert.True(holding.Length == 0, "a Context7 key sits in: " + string.Join(", ", holding));
    }

    /// <summary>A server list at the root names a key by its variable, never by its value.</summary>
    [Fact]
    public void A_server_list_at_the_root_carries_no_bearer_value()
    {
        string path = Path.Combine(Repo.Root(), ".mcp.json");
        if (!File.Exists(path))
        {
            return;
        }

        string servers = File.ReadAllText(path);
        Assert.DoesNotMatch(new Regex(@"Bearer\s+(?!\$\{)[A-Za-z0-9_-]{8,}", RegexOptions.CultureInvariant), servers);
        Assert.DoesNotMatch(Key, servers);
    }
    // #endregion the-key
}
