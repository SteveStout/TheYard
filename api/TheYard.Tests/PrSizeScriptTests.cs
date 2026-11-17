using System.Diagnostics;

namespace TheYard.Tests;

/// <summary>
/// <c>scripts/pr_size.sh</c> run for real against a planted repository (ADR: A pull request of AI-written code is sized
/// to the gate): a sliver with no trailer is refused, a sliver that names why it ships alone passes, a version inside the
/// band passes, and a large one with no trailer is refused. The files the promote and the fill write are not counted, and
/// the band is read from <c>.github/pr-size.json</c>, the one place it is written.
/// </summary>
public sealed class PrSizeScriptTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "prsize-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_work, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder left behind is the system's to clear.
        }
    }

    // #region the-sandbox
    private (int Code, string Output) Run(string file, string args)
    {
        var start = new ProcessStartInfo(file, args) { WorkingDirectory = _work, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["GIT_AUTHOR_NAME"] = start.Environment["GIT_COMMITTER_NAME"] = "Gate Test";
        start.Environment["GIT_AUTHOR_EMAIL"] = start.Environment["GIT_COMMITTER_EMAIL"] = "gate@example.com";
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + errors.GetAwaiter().GetResult());
    }

    private void Git(string args) => Assert.Equal(0, Run("git", args).Code);

    private void Lines(string path, int count)
    {
        string full = Path.Combine(_work, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllLines(full, Enumerable.Range(0, count).Select(i => $"line {i}"));
    }

    /// <summary>A repository holding the script and the band, its base tagged, and one commit on it carrying <paramref name="lines"/> counted lines.</summary>
    private (int Code, string Output) Version(int lines, string message)
    {
        Directory.CreateDirectory(Path.Combine(_work, "scripts"));
        Directory.CreateDirectory(Path.Combine(_work, ".github"));
        File.Copy(Path.Combine(Repo.Root(), "scripts", "pr_size.sh"), Path.Combine(_work, "scripts", "pr_size.sh"));
        File.Copy(Path.Combine(Repo.Root(), ".github", "pr-size.json"), Path.Combine(_work, ".github", "pr-size.json"));
        Git("init -q -b main");
        Git("add -A");
        Git("commit -q -m base");
        Git("tag base");
        Lines("src/change.ts", lines);
        // Written by the promote and the fill, so never counted, however large.
        Lines("data/test-results.json", 5_000);
        Lines("docs/CHANGELOG.md", 3);
        Git("add -A");
        File.WriteAllText(Path.Combine(_work, "message.txt"), message);
        Git("commit -q -F message.txt");
        return Run("bash", "scripts/pr_size.sh base");
    }
    // #endregion the-sandbox

    [Fact]
    public void A_sliver_with_no_trailer_is_refused_and_told_to_ride_in_the_next_version()
    {
        var (code, output) = Version(12, "TheYard 1.0.5.90: a small thing");

        Assert.Equal(1, code);
        Assert.Contains("pr size: 12 lines changed against base (band 100 to 1000): REFUSED, under the floor of 100", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sliver_that_names_why_it_ships_alone_passes()
    {
        var (code, output) = Version(12, "TheYard 1.0.5.90: a workflow fix\n\nShips-alone: it changes a workflow\n");

        Assert.Equal(0, code);
        Assert.Contains("under the floor, ships alone: it changes a workflow", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_version_inside_the_band_passes_and_the_promote_s_files_are_not_counted()
    {
        var (code, output) = Version(240, "TheYard 1.0.5.90: a whole item");

        Assert.Equal(0, code);
        Assert.Contains("pr size: 240 lines changed against base (band 100 to 1000): inside the band", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_large_version_with_no_trailer_is_refused()
    {
        var (code, output) = Version(1_200, "TheYard 1.0.5.90: everything at once");

        Assert.Equal(1, code);
        Assert.Contains("REFUSED, over the ceiling of 1000 with no Large: trailer", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_band_is_written_once_in_its_own_file()
    {
        string band = File.ReadAllText(Path.Combine(Repo.Root(), ".github", "pr-size.json"));

        Assert.Contains("\"floor\": 100", band, StringComparison.Ordinal);
        Assert.Contains("\"ceiling\": 1000", band, StringComparison.Ordinal);
        // The script reads the two numbers from the file and types neither.
        Assert.DoesNotContain("100", File.ReadAllText(Path.Combine(Repo.Root(), "scripts", "pr_size.sh")), StringComparison.Ordinal);
    }
}
