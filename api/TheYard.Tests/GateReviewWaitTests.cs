using System.Diagnostics;
using System.Text.Json;

namespace TheYard.Tests;

/// <summary>
/// The two scripts around CodeRabbit's review run for real against a stand-in gh (ADR-129, the addendum of 1.0.5.8):
/// <c>scripts/coderabbit_review.py</c>'s wait, which the gate's coderabbit job runs, and <c>scripts/gate_pr.sh</c>,
/// which opens the gate's request and closes it on main's commit.
///
/// <para>The cold review of 1.0.4.176 and 1.0.4.177 asked for these: the fold, the missing row and the count were held,
/// and the loop that decides whether a review came, and the script that makes the request it comes on, were not. Each
/// case answers as GitHub would and reads the row or the calls the script made.</para>
/// </summary>
public sealed class GateReviewWaitTests : IDisposable
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "review-wait-" + Guid.NewGuid().ToString("N"));

    /// <summary>The planted answers go with the case that planted them.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder left behind is the system's to clear.
        }
    }

    // #region the-sandbox
    private string Bin => Path.Combine(_root, "bin");

    private void Answer(string name, object value) => File.WriteAllText(Path.Combine(_root, name), JsonSerializer.Serialize(value));

    /// <summary>A stand-in gh that answers each API path from a planted file and writes down every other call.</summary>
    private void Gh()
    {
        Directory.CreateDirectory(Bin);
        string gh = Path.Combine(Bin, "gh");
        File.WriteAllText(gh, "#!/usr/bin/env bash\necho \"$*\" >> \"$ROOT/calls.log\"\ncase \"$*\" in\n"
            + "  *\"pulls?state=open\"*) cat \"$ROOT/open.json\" 2>/dev/null || echo '[[]]';;\n"
            + "  *\"pulls?state=all\"*) cat \"$ROOT/all.json\" 2>/dev/null || echo '[[]]';;\n"
            + "  *check-runs*) cat \"$ROOT/checks.json\" 2>/dev/null || echo '[{\"check_runs\":[]}]';;\n"
            + "  *\"/reviews\"*) cat \"$ROOT/reviews.json\" 2>/dev/null || echo '[[]]';;\n"
            + "  *\"/comments\"*) cat \"$ROOT/comments.json\" 2>/dev/null || echo '[[]]';;\n"
            + "  \"api repos/\"*\"/commits/gate/\"*) echo 'TheYard 1.0.4.900: a line';;\n"
            + "  \"pr list\"*) if [ -n \"${FAKE_OPEN:-}\" ]; then echo \"$FAKE_OPEN\"; elif [ -f \"$ROOT/created\" ]; then echo 78; fi;;\n"
            + "  \"pr create\"*) touch \"$ROOT/created\";;\n"
            + "  \"pr comment\"*|\"pr close\"*) ;;\n"
            + "  *) exit 1;;\nesac\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(gh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private (int Code, string Output) Run(string file, string args, string open = "")
    {
        var start = new ProcessStartInfo(file, args) { WorkingDirectory = _root, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["PATH"] = Bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        start.Environment["ROOT"] = _root;
        start.Environment["GITHUB_REPOSITORY"] = "SteveStout/TheYard";
        start.Environment["CODERABBIT_NO_REQUEST_AFTER"] = "1";
        start.Environment["CODERABBIT_POLL_SECONDS"] = "0.2";
        start.Environment["FAKE_OPEN"] = open;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(1)))
        {
            process.Kill(entireProcessTree: true);
            return (-1, "the script did not finish in a minute");
        }
        return (process.ExitCode, output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult());
    }

    /// <summary>The wait as the gate's coderabbit job runs it, with a ceiling of <paramref name="ceiling"/> seconds, and the row it wrote.</summary>
    private JsonElement Wait(int ceiling)
    {
        string row = Path.Combine(_root, "row.json");
        var (code, output) = Run("python3", $"\"{Path.Combine(Repo.Root(), "scripts", "coderabbit_review.py")}\" gate/1.0.4.900 {Sha} {ceiling} \"{row}\"");
        Assert.True(code == 0, output);
        using var read = JsonDocument.Parse(File.ReadAllText(row));
        return read.RootElement.Clone();
    }

    private static object Check(string status, string title) => new[] { new { check_runs = new[] { new { app = new { slug = "coderabbitai" }, status, conclusion = "success", started_at = "2026-11-10T22:00:00Z", completed_at = "2026-11-10T22:10:00Z", output = new { title, summary = "" } } } } };

    private string Calls() => File.Exists(Path.Combine(_root, "calls.log")) ? File.ReadAllText(Path.Combine(_root, "calls.log")) : "";
    // #endregion the-sandbox

    // #region the-wait
    /// <summary>The control: a request, a completed review, its findings read and counted on the commit.</summary>
    [Fact]
    public void A_completed_review_is_read_and_its_findings_counted()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Gh();
        Answer("open.json", new[] { new[] { new { number = 42, head = new { sha = Sha } } } });
        Answer("all.json", new[] { new[] { new { number = 42, head = new { sha = Sha } } } });
        Answer("checks.json", Check("completed", "Review completed"));
        Answer("comments.json", new[] { new[] { new { user = new { login = "coderabbitai[bot]" }, commit_id = Sha, body = "**<picture></picture> Minor** · a finding" } } });

        JsonElement row = Wait(30);

        Assert.Equal("full", row.GetProperty("reviewed").GetString());
        Assert.Equal(42, row.GetProperty("pr").GetInt32());
        Assert.Equal(1, row.GetProperty("findings").GetProperty("minor").GetInt32());
        Assert.Equal(600, row.GetProperty("review_seconds").GetInt32());
    }

    /// <summary>No request at all stops the wait early and says so, rather than waiting out the ceiling behind a notice.</summary>
    [Fact]
    public void No_request_stops_the_wait_early_and_says_so()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Gh();

        JsonElement row = Wait(30);

        Assert.Equal("not reviewed, no pull request", row.GetProperty("reviewed").GetString());
        Assert.True(row.GetProperty("waited_seconds").GetInt32() < 30, "the wait should stop at the no-request mark, not the ceiling");
    }

    /// <summary>A review that never completes inside the ceiling is written as such, and a skipped check is not a completed one.</summary>
    [Theory]
    [InlineData("in_progress", "Review in progress")]
    [InlineData("completed", "Review skipped")]
    public void A_review_that_does_not_complete_inside_the_ceiling_says_so(string status, string title)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Gh();
        Answer("open.json", new[] { new[] { new { number = 42, head = new { sha = Sha } } } });
        Answer("checks.json", Check(status, title));

        JsonElement row = Wait(2);

        Assert.Equal("not reviewed, no completed review inside the ceiling", row.GetProperty("reviewed").GetString());
        Assert.Equal(42, row.GetProperty("pr").GetInt32());
    }
    // #endregion the-wait

    // #region the-request
    /// <summary>`open` makes the request when there is none, titled with the commit's subject, reuses one that is open, and asks for the review either way.</summary>
    [Theory]
    [InlineData("", "78", true)]
    [InlineData("77", "77", false)]
    public void Open_makes_or_reuses_the_request_and_asks_for_the_review(string open, string pr, bool created)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Gh();

        var (code, output) = Run("bash", $"\"{Path.Combine(Repo.Root(), "scripts", "gate_pr.sh")}\" open 1.0.4.900", open);

        Assert.True(code == 0, output);
        Assert.Contains($"pull request {pr} for gate/1.0.4.900, review asked", output, StringComparison.Ordinal);
        Assert.Equal(created, Calls().Contains("pr create -R SteveStout/TheYard --base main --head gate/1.0.4.900 --title TheYard 1.0.4.900: a line", StringComparison.Ordinal));
        Assert.Contains($"pr comment {pr} -R SteveStout/TheYard --body @coderabbitai review", Calls(), StringComparison.Ordinal);
    }

    /// <summary>`close` closes the open request on main's commit; with none open it says so and closes nothing.</summary>
    [Theory]
    [InlineData("77", "pull request 77 closed on abcdef12")]
    [InlineData("", "no open pull request for gate/1.0.4.900")]
    public void Close_closes_the_open_request_on_main_s_commit_or_says_there_is_none(string open, string said)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Gh();

        var (code, output) = Run("bash", $"\"{Path.Combine(Repo.Root(), "scripts", "gate_pr.sh")}\" close 1.0.4.900 abcdef1234567890", open);

        Assert.True(code == 0, output);
        Assert.Contains(said, output, StringComparison.Ordinal);
        Assert.Equal(open.Length > 0, Calls().Contains("pr close 77", StringComparison.Ordinal));
    }
    // #endregion the-request
}
