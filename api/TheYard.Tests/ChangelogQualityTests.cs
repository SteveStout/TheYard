using TheYard.Api.Features.Docs;

namespace TheYard.Tests;

/// <summary>
/// The changelog as the site serves it ends each measured version's line with its quality reading against the
/// version before (ChangelogQuality): held, worse and better counted the way the landing and the gate badge count
/// them, a version the history did not measure left as written, and the file on GitHub never changed.
/// </summary>
public sealed class ChangelogQualityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"changelog-quality-{Guid.NewGuid():N}");

    public ChangelogQualityTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "data"));
        File.WriteAllText(Path.Combine(root, "data", "quality-history.json"), """
            {"rows": [
              {"version": "1.0.0.1", "source": "quality.py", "numbers": {"files_over_300": 2, "dotnet_tests_higher_is_better": 10}},
              {"version": "1.0.0.2", "source": "quality.py", "numbers": {"files_over_300": 3, "dotnet_tests_higher_is_better": 12}},
              {"version": "1.0.0.3", "source": "quality.py", "numbers": {"files_over_300": 3, "dotnet_tests_higher_is_better": 12}}
            ]}
            """);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void Each_measured_version_ends_with_its_reading_against_the_version_before()
    {
        string changelog = string.Join('\n',
            "# Changelog",
            "- **1.0.0.4** (2026-11-17): Not measured yet.",
            "- **1.0.0.3** (2026-11-17): Nothing moved.",
            "- **1.0.0.2** (2026-11-16): One rose, one got better.",
            "- **1.0.0.1** (2026-11-15): The first reading.");

        string served = ChangelogQuality.Append(ChangelogQuality.File, changelog, root);

        Assert.Contains("- **1.0.0.4** (2026-11-17): Not measured yet.\n", served, StringComparison.Ordinal);
        Assert.Contains("- **1.0.0.3** (2026-11-17): Nothing moved. *Quality: 2 of 2 held*.", served, StringComparison.Ordinal);
        Assert.Contains("- **1.0.0.2** (2026-11-16): One rose, one got better. *Quality: 1 of 2 held, 1 worse, 1 better*.", served, StringComparison.Ordinal);
        Assert.Contains("- **1.0.0.1** (2026-11-15): The first reading. *Quality: 2 of 2 held*.", served, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_document_is_served_as_written()
    {
        const string page = "- **1.0.0.1** (2026-11-15): Words.";
        Assert.Equal(page, ChangelogQuality.Append("docs/quality/QUALITY.md", page, root));
    }
}
