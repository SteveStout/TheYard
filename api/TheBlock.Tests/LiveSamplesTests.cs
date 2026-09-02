using Microsoft.AspNetCore.Mvc.Testing;
using TheBlock.Api;

namespace TheBlock.Tests;

/// <summary>
/// Live code samples (ADR-014): the whitelist is a string check, a live block
/// expands into the region with a language tag and a source line, and every
/// failure renders a one-line note instead of an error.
/// </summary>
public class LiveSamplesTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly List<string> _tempRoots = [];

    /// <summary>Every throwaway repo root is deleted when the test class is done (ADR-017).</summary>
    public void Dispose()
    {
        foreach (string root in _tempRoots)
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>A throwaway repo root with one whitelisted file and one off-root file.</summary>
    private string TempRepo()
    {
        string root = Path.Combine(Path.GetTempPath(), "theyard-live-" + Guid.NewGuid().ToString("N"));
        _tempRoots.Add(root);
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(
            Path.Combine(root, "src", "sample.ts"),
            "const before = 1;\n// #region greet\nexport function greet(name: string) {\n  return `hi ${name}`;\n}\n// #endregion greet\nconst after = 2;\n");
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        File.WriteAllText(Path.Combine(root, "docs", "secret.md"), "# not for serving\n");
        return root;
    }

    // #region rejection
    [Theory]
    [InlineData("../README.md")]
    [InlineData("/etc/passwd")]
    [InlineData("src/../docs/secret.md")]
    [InlineData("docs/secret.md")]
    [InlineData("api\\TheBlock.Api\\Program.cs")]
    [InlineData("src/")]
    [InlineData("")]
    [InlineData(".env")]
    [InlineData("package-lock.json")]
    [InlineData("docs/CHANGELOG.md")]
    [InlineData("Dockerfile.bak")]
    public void Paths_off_the_roots_or_with_escapes_are_rejected_as_strings(string path)
    {
        Assert.False(LiveSamples.IsAllowedPath(path));
    }
    // #endregion rejection

    [Theory]
    [InlineData("src/components/DocsMenu.tsx")]
    [InlineData("api/TheBlock.Api/Program.cs")]
    [InlineData("infra/main.bicep")]
    [InlineData(".github/workflows/deploy.yml")]
    [InlineData("tests/e2e/mobile.spec.ts")]
    [InlineData("edge/_redirects")]
    [InlineData("Dockerfile")]
    [InlineData("netlify.toml")]
    [InlineData("tsconfig.app.json")]
    [InlineData(".editorconfig")]
    public void Paths_under_the_roots_and_the_named_root_files_are_allowed(string path)
    {
        Assert.True(LiveSamples.IsAllowedPath(path));
    }

    [Fact]
    public void A_live_block_expands_into_the_region_with_a_language_tag_and_a_source_line()
    {
        string root = TempRepo();
        string doc = "# Doc\n\nBefore.\n\n```live path=src/sample.ts region=greet\n```\n\nAfter.\n";

        string expanded = LiveSamples.Expand(doc, root, "abc1234");

        Assert.Contains("```ts\nexport function greet(name: string) {\n  return `hi ${name}`;\n}\n```", expanded);
        Assert.Contains("read from this build at abc1234", expanded);
        Assert.Contains("blob/abc1234/src/sample.ts#L3-L5", expanded);
        Assert.DoesNotContain("```live", expanded);
        Assert.DoesNotContain("const before", expanded);
        Assert.StartsWith("# Doc\n\nBefore.\n\n", expanded);
        Assert.EndsWith("\n\nAfter.\n", expanded);
    }

    [Fact]
    public void A_star_region_shows_the_whole_file_with_a_first_line_link()
    {
        string root = TempRepo();
        string doc = "```live path=src/sample.ts region=*\n```\n";

        string expanded = LiveSamples.Expand(doc, root, "abc1234");

        Assert.StartsWith("```ts\nconst before = 1;\n", expanded);
        Assert.Contains("const after = 2;\n```\n", expanded);
        Assert.Contains("blob/abc1234/src/sample.ts#L1-L7", expanded);
        Assert.DoesNotContain("Sample unavailable", expanded);
    }

    [Fact]
    public void A_rejected_path_renders_a_note_without_touching_the_filesystem()
    {
        // The repo root does not exist, so any filesystem touch would throw.
        string expanded = LiveSamples.Expand("```live path=../secret.md region=x\n```", Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N")), "local");

        Assert.Contains("*Sample unavailable:", expanded);
        Assert.Contains("outside the allowed roots", expanded);
    }

    [Fact]
    public void An_existing_file_off_the_roots_is_still_rejected()
    {
        string root = TempRepo();
        string expanded = LiveSamples.Expand("```live path=docs/secret.md region=x\n```", root, "local");

        Assert.Contains("*Sample unavailable:", expanded);
        Assert.DoesNotContain("not for serving", expanded);
    }

    [Fact]
    public void Missing_file_missing_region_and_no_region_render_notes_and_never_throw()
    {
        string root = TempRepo();

        string missingFile = LiveSamples.Expand("```live path=src/nope.ts region=greet\n```", root, "local");
        Assert.Contains("*Sample unavailable:", missingFile);
        Assert.Contains("not in this build", missingFile);

        string missingRegion = LiveSamples.Expand("```live path=src/sample.ts region=farewell\n```", root, "local");
        Assert.Contains("*Sample unavailable:", missingRegion);
        Assert.Contains("was not found", missingRegion);

        string noRegion = LiveSamples.Expand("```live path=src/sample.ts\n```", root, "local");
        Assert.Contains("*Sample unavailable:", noRegion);
    }

    [Fact]
    public void A_doc_without_live_blocks_passes_through_untouched()
    {
        string doc = "# Plain\n\n```ts\nconst x = 1;\n```\n\nDone.\n";
        Assert.Equal(doc, LiveSamples.Expand(doc, TempRepo(), "local"));
    }

    [Fact]
    public async Task Served_records_carry_expanded_samples_and_never_the_live_fence()
    {
        string phone = await _client.GetStringAsync("/api/docs/adr-phone");
        Assert.DoesNotContain("```live", phone);
        Assert.Contains("```tsx\n", phone);
        Assert.Contains("export const MENU_ORDER", phone);

        string live = await _client.GetStringAsync("/api/docs/adr-live-samples");
        Assert.DoesNotContain("```live", live);
        Assert.Contains("```csharp\n", live);
        Assert.Contains("public static string Expand(", live);
        Assert.Contains("*Live from [`api/TheBlock.Api/LiveSamples.cs`]", live);
    }
}
