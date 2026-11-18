using System.Diagnostics;

namespace TheYard.Tests;

/// <summary>
/// The blog slice's coverage floor counts what coverlet wrote (ADR: Every LinkedIn post is also a blog post, the addendum
/// on the read model): coverlet writes a line's branches once per method on it and copies them into the class's own
/// lines, so the count sums the methods' and reads the class's lines only for the line total.
/// </summary>
public sealed class BlogCoverageScriptTests : IDisposable
{
    private readonly string _work = Directory.CreateTempSubdirectory("theyard-blogcoverage-").FullName;

    private static string Report(string methods, string classLines) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <coverage line-rate="1" branch-rate="1">
          <packages><package name="TheYard.Domain" line-rate="1" branch-rate="1"><classes>
            <class name="TheYard.Domain.Features.Blog.Rules" filename="/src/api/TheYard.Domain/Features/Blog/Rules.cs" line-rate="1" branch-rate="1">
              <methods>{methods}</methods>
              <lines>{classLines}</lines>
            </class>
          </classes></package></packages>
        </coverage>
        """;

    private static string Method(string name, string conditions) =>
        $"""<method name="{name}" signature="()"><lines><line number="7" hits="1" branch="True" condition-coverage="{conditions}" /></lines></method>""";

    private (int Code, string Output) Run(string xml)
    {
        File.WriteAllText(Path.Combine(_work, "coverage.cobertura.xml"), xml);
        var start = new ProcessStartInfo("python3", $"\"{Path.Combine(Repo.Root(), ".github", "coverage.py")}\" \"{_work}\" 0 0") { RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    [Fact]
    public void Two_methods_on_one_line_each_count_their_branches_and_the_class_copy_counts_none()
    {
        string methods = Method("Score", "100% (2/2)") + Method("ScoreLambda", "0% (0/4)");
        string copy = """<line number="7" hits="1" branch="True" condition-coverage="33% (2/6)" />""";
        var (code, output) = Run(Report(methods, copy));
        Assert.Contains("blog slice: 100.0% of lines, 33.3% of branches", output, StringComparison.Ordinal);
        Assert.Equal(1, code);
    }

    [Fact]
    public void A_slice_every_branch_of_which_is_taken_passes_its_floor()
    {
        string copy = """<line number="7" hits="1" branch="True" condition-coverage="100% (2/2)" />""";
        var (code, output) = Run(Report(Method("Score", "100% (2/2)"), copy));
        Assert.Contains("blog slice: 100.0% of lines, 100.0% of branches", output, StringComparison.Ordinal);
        Assert.Equal(0, code);
    }

    public void Dispose() => Directory.Delete(_work, recursive: true);
}
