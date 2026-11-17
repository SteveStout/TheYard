using System.Diagnostics;

namespace TheYard.Tests;

/// <summary>
/// <c>scripts/fill_version.sh</c> run for real against a bare repository standing in for GitHub (ADR-031, the addendum
/// on the fill at the claim). The number a version ships under is filled at the claim, under the gate lock, from main's
/// top line plus one: the version, the date and the record number in the files, in a file's name and in the commit
/// message; the ratchet only tightened; the commit amended; and a second run changes nothing.
///
/// <para>The placeholders are spelled in two parts here, as the script spells them, because the fill replaces every
/// whole one in the tree, this file's included.</para>
/// </summary>
public sealed class FillVersionScriptTests : IDisposable
{
    private static readonly string Ver = "@" + "VER@";
    private static readonly string Day = "@" + "DATE@";
    private static readonly string Adr = "@" + "ADR@";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "fill-" + Guid.NewGuid().ToString("N"));

    private string Work => Path.Combine(_root, "work");

    public void Dispose()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder left behind is the system's to clear.
        }
    }

    // #region the-sandbox
    private static (int Code, string Output) Run(string dir, string file, string args, IReadOnlyDictionary<string, string>? env = null)
    {
        var start = new ProcessStartInfo(file, args) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["GIT_AUTHOR_NAME"] = start.Environment["GIT_COMMITTER_NAME"] = "Gate Test";
        start.Environment["GIT_AUTHOR_EMAIL"] = start.Environment["GIT_COMMITTER_EMAIL"] = "gate@example.com";
        start.Environment.Remove("OWNER_YES");
        foreach (var (name, value) in env ?? new Dictionary<string, string>())
        {
            start.Environment[name] = value;
        }
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + errors.GetAwaiter().GetResult());
    }

    private string Git(string args)
    {
        var (code, output) = Run(Work, "git", args);
        Assert.True(code == 0, $"git {args}: {output}");
        return output.Trim();
    }

    private void Write(string path, string text)
    {
        string full = Path.Combine(Work, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private string Read(string path) => File.ReadAllText(Path.Combine(Work, path));

    private static string Changelog(params string[] versions) =>
        "# Changelog\n\n" + string.Concat(versions.Select(v => $"- **{v}** (2026-10-27): a line.\n"));

    /// <summary>
    /// Main on 1.0.4.105 with records up to ADR-041, the quality scan, and a ratchet with one limit looser than the
    /// tree and one floor the tree is under, in a bare origin and a clone of it holding the scripts.
    /// </summary>
    private void Main()
    {
        Directory.CreateDirectory(_root);
        Assert.Equal(0, Run(_root, "git", "init -q --bare -b main origin.git").Code);
        Assert.Equal(0, Run(_root, "git", "clone -q origin.git work").Code);
        Git("checkout -q -b main");
        foreach (string script in new[] { "versions.sh", "fill_version.sh" })
        {
            Directory.CreateDirectory(Path.Combine(Work, "scripts"));
            File.Copy(Path.Combine(Repo.Root(), "scripts", script), Path.Combine(Work, "scripts", script));
        }
        Directory.CreateDirectory(Path.Combine(Work, ".github"));
        File.Copy(Path.Combine(Repo.Root(), ".github", "quality.py"), Path.Combine(Work, ".github", "quality.py"));
        Write(".github/quality-ratchet.json", "{\n \"undocumented_declarations\": 7,\n \"test_to_prod_ratio_higher_is_better\": 0.5\n}\n");
        Write("docs/CHANGELOG.md", Changelog("1.0.4.105", "1.0.4.104"));
        Write("docs/decisions/ADR-009-one.md", "# ADR: one\n");
        Write("docs/decisions/ADR-041-two.md", "# ADR: two\n");
        Git("add -A");
        Git("commit -q -m main");
        Git("push -q origin main");
    }

    /// <summary>One commit on main carrying every placeholder: the changelog, a record named and numbered by it, and the message.</summary>
    private void Unfilled()
    {
        Write("docs/CHANGELOG.md", $"# Changelog\n\n- **{Ver}** ({Day}): a line, with ADR-{Adr}.\n- **1.0.4.105** (2026-10-27): a line.\n- **1.0.4.104** (2026-10-27): a line.\n");
        Write($"docs/decisions/ADR-{Adr}-three.md", $"# ADR: three\n\n## Addendum, {Day} ({Ver}): the record ADR-{Adr}\n");
        Git("add -A");
        Git($"commit -q -m \"TheYard {Ver}: a thing\" -m \"Body on {Day}, record ADR-{Adr}.\"");
    }

    private (int Code, string Output) Fill(IReadOnlyDictionary<string, string>? env = null) => Run(Work, "bash", "scripts/fill_version.sh", env);

    private static string Today() =>
        TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "America/Chicago").ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    // #endregion the-sandbox

    // #region the-cases
    /// <summary>The control: every placeholder filled from main plus one, the record renamed, the ratchet tightened, one commit amended.</summary>
    [Fact]
    public void The_fill_numbers_the_version_the_date_and_the_record_from_main()
    {
        // The script runs on Linux; a Windows checkout has no bash to give it.
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        Unfilled();
        string today = Today();

        var (code, output) = Fill();

        Assert.True(code == 0, output);
        Assert.Contains($"VERSION=1.0.4.106 ADR=042 HEAD={Git("rev-parse --short=8 HEAD")}", output, StringComparison.Ordinal);
        Assert.StartsWith($"# Changelog\n\n- **1.0.4.106** ({today}): a line, with ADR-042.\n", Read("docs/CHANGELOG.md"), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Work, "docs", "decisions", $"ADR-{Adr}-three.md")));
        Assert.Equal($"# ADR: three\n\n## Addendum, {today} (1.0.4.106): the record ADR-042\n", Read("docs/decisions/ADR-042-three.md"));
        Assert.Equal($"TheYard 1.0.4.106: a thing\n\nBody on {today}, record ADR-042.", Git("log -1 --format=%B"));
        Assert.Equal(Git("rev-parse origin/main"), Git("rev-parse HEAD~1"));
        Assert.Equal("", Git("status --porcelain"));
        // A limit the tree beats is lowered to it; a floor the tree is under is never loosened.
        Assert.Equal("{\n \"undocumented_declarations\": 0,\n \"test_to_prod_ratio_higher_is_better\": 0.5\n}\n", Read(".github/quality-ratchet.json"));
        Assert.Equal(1, Run(Work, "git", $"grep -q -F -e {Ver} -e {Day} -e {Adr}").Code);
    }

    /// <summary>Run twice, the second run changes nothing: the same commit, and no record number to fill.</summary>
    [Fact]
    public void A_second_fill_changes_nothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        Unfilled();
        Assert.Equal(0, Fill().Code);
        string head = Git("rev-parse HEAD");

        var (code, output) = Fill();

        Assert.True(code == 0, output);
        Assert.Equal(head, Git("rev-parse HEAD"));
        Assert.Contains("nothing to fill: the commit already names 1.0.4.106", output, StringComparison.Ordinal);
        Assert.Contains("VERSION=1.0.4.106 ADR=none", output, StringComparison.Ordinal);
    }

    /// <summary>What the fill must refuse is refused in one sentence, and the commit stays as it was.</summary>
    [Theory]
    [InlineData("two-commits", "HEAD is not exactly one commit on origin/main")]
    [InlineData("dirty", "the tree has changes that are not committed")]
    [InlineData("filled-for-another", "the changelog's top line is '1.0.4.107' and it must be the version placeholder or 1.0.4.106")]
    [InlineData("no-line-of-its-own", "the changelog's top line is '1.0.4.105'")]
    public void A_commit_the_fill_cannot_number_is_refused(string shape, string said)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        switch (shape)
        {
            case "two-commits":
                Unfilled();
                Write("other.txt", "another commit\n");
                Git("add -A");
                Git("commit -q -m another");
                break;
            case "dirty":
                Unfilled();
                Write("docs/CHANGELOG.md", Read("docs/CHANGELOG.md") + "a change not committed\n");
                break;
            case "filled-for-another":
                Write("docs/CHANGELOG.md", Changelog("1.0.4.107", "1.0.4.105", "1.0.4.104"));
                Git("commit -q -am filled");
                break;
            default:
                Write("other.txt", "a commit with no changelog line\n");
                Git("add -A");
                Git("commit -q -m bare");
                break;
        }
        string head = Git("rev-parse HEAD");

        var (code, output) = Fill();

        Assert.True(code != 0, output);
        Assert.Contains("fill REFUSED: " + said, output, StringComparison.Ordinal);
        Assert.Equal(head, Git("rev-parse HEAD"));
    }

    /// <summary>The owner's move of the third number is filled only when OWNER_YES names it, as versions.sh allows.</summary>
    [Theory]
    [InlineData("1.0.5.0", true)]
    [InlineData("1.0.6.0", false)]
    public void The_owners_move_is_filled_only_when_owner_yes_names_it(string yes, bool fills)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        Unfilled();

        var (code, output) = Fill(new Dictionary<string, string> { ["OWNER_YES"] = yes });

        Assert.True(fills == (code == 0), output);
        Assert.Contains(fills ? "VERSION=1.0.5.0" : "the owner's move from main's 1.0.4.105 is 1.0.5.0", output, StringComparison.Ordinal);
    }
    // #endregion the-cases
}
