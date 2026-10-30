namespace TheYard.Tests;

/// <summary>
/// The projects whose documentation is finished, and the rule that keeps them finished.
///
/// <para>Declarations with no summary are one of the fourteen counts that may only fall (ADR: The
/// numbers that may only fall), and a count on its own has a weakness: it falls when one project is
/// paid down and stays level when that project fills back up and another is paid down in its place.
/// So each instalment that takes a project to zero adds that project's name here, and from then on
/// the question asked of it is not "how many" but "any", which is a question with one right
/// answer.</para>
///
/// <para>The rule body is <c>tests/rules/SummaryRule.cs</c>, shared with the job hunt's suite so the
/// Function's own projects can be named here in the same way when their instalment lands, and it
/// asks exactly what the ratchet's scanner asks, so a project this file calls finished is a project
/// the scanner counts as nothing (ADR: The rules a change has to pass).</para>
/// </summary>
public sealed class MemberSummaryTests
{
    // #region finished-projects
    /// <summary>
    /// The projects under <c>api/</c> every declaration of which says what it does. A name is added by
    /// the instalment that takes that project to zero, and a name is never taken off: the whole point
    /// of the list is that finished stays finished.
    /// </summary>
    private static readonly string[] Finished = ["TheYard.Infrastructure"];
    // #endregion finished-projects

    /// <summary>Folders that hold no declaration a reader of this codebase meets: build output, generated schema history, and the suites.</summary>
    private static readonly string[] Skipped = ["bin", "obj", "Migrations", "TheYard.Tests", "TheYard.Experiment"];

    [Fact]
    public void Every_declaration_in_a_finished_project_says_what_it_does()
    {
        string root = Repo.Root();
        List<string> files = [.. Finished.SelectMany(project => Sources(Path.Combine(root, "api", project)))];

        // A scan that read nothing would pass with no summaries written at all, which is the one
        // failure this test cannot see from its own result.
        int declarations = TheYard.Rules.SummaryRule.Declarations(files);
        Assert.True(declarations > 300, $"the scan read {declarations} declarations across {Finished.Length} finished project(s), which is too few to trust");

        List<string> undocumented = TheYard.Rules.SummaryRule.Undocumented(files, root);
        Assert.True(
            undocumented.Count == 0,
            $"{undocumented.Count} declaration(s) in a project this suite calls finished carry no summary:"
                + Environment.NewLine + string.Join(Environment.NewLine, undocumented));
    }

    /// <summary>Every project named as finished is a project that is there, so the list cannot outlive what it names.</summary>
    [Fact]
    public void Every_project_named_as_finished_exists()
    {
        string api = Path.Combine(Repo.Root(), "api");
        var missing = Finished.Where(project => !Directory.Exists(Path.Combine(api, project))).ToList();
        Assert.True(missing.Count == 0, "named as finished but not there: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The rule tells a documented declaration from an undocumented one, and does not mind an attribute,
    /// a region marker or a blank line standing between a summary and the thing it describes. Held
    /// against text rather than against the checkout, so it stays a test of the rule once every project
    /// is finished and the checkout has nothing left for it to find.
    /// </summary>
    [Fact]
    public void The_rule_reads_a_summary_through_what_may_sit_under_it()
    {
        string[] lines =
        [
            "/// <summary>What this one holds, and why it is kept.</summary>",
            "public required string Documented { get; set; }",
            "",
            "public required string Bare { get; set; }",
            "",
            "/// <summary>Through a blank line, a region marker and an attribute.</summary>",
            "// #region rows",
            "",
            "[Column(\"through\")]",
            "public required string Through { get; set; }",
            "",
            "public const int NotCounted = 3;",
        ];

        var found = TheYard.Rules.SummaryRule.UndocumentedIn(lines);

        Assert.Equal(["public required string Bare { get; set; }"], found.Select(one => one.Text));
        Assert.Equal(4, found[0].Line);
    }

    /// <summary>Every C# source file of one project, leaving out build output, the generated schema history and the suites.</summary>
    /// <param name="project">The project's folder.</param>
    private static IEnumerable<string> Sources(string project) =>
        Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".g.cs", StringComparison.Ordinal))
            .Where(path => !Skipped.Any(folder =>
                path.Contains($"{Path.DirectorySeparatorChar}{folder}{Path.DirectorySeparatorChar}", StringComparison.Ordinal)));
}
