using TestProject.Documentation;

namespace TestProject.Tests;

/// <summary>
/// Checks how the app finds the version and commit it reports. The version is the first bulleted
/// line of the changelog, and the commit is read from the .git folder by following HEAD to its
/// branch file. When either source is missing the answer is "unknown", because a guessed value
/// would mislead anyone checking which build is running.
/// (more in docs/ADR-012-documents-served-by-the-app.md)
/// </summary>
public sealed class VersionReaderTests
{
    [Fact]
    public void The_first_listed_version_wins_and_a_missing_log_is_unknown()
    {
        using var home = new TempHome();
        string log = home.File("CHANGELOG.md", "# Changelog\n\nintro 9.9.9.9 in prose is not a line\n\n- 1.0.0.3 newest.\n- 1.0.0.2 older.\n");
        Assert.Equal("1.0.0.3", VersionReader.VersionFrom(log));
        Assert.Equal(VersionReader.Unknown, VersionReader.VersionFrom(Path.Combine(home.Root, "missing.md")));
    }

    [Fact]
    public void A_folder_with_no_git_reports_unknown_and_a_ref_is_followed()
    {
        using var home = new TempHome();
        Assert.Equal(VersionReader.Unknown, VersionReader.CommitFrom(home.Root));
        home.File(".git/HEAD", "ref: refs/heads/main\n");
        home.File(".git/refs/heads/main", "0123456789abcdef0123456789abcdef01234567\n");
        Assert.Equal("0123456", VersionReader.CommitFrom(home.Root));
    }
}
