using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The job hunt's Function in rings (ADR-117: One host per process, the rings under them): the rules in
/// JobHunt.Domain, the use cases and their ports in JobHunt.Application, the adapters in JobHunt.Infrastructure,
/// and the host over them. Each ring may lean only inward, only Infrastructure talks to Google, GitHub, the
/// share or the campaign's database, nothing under the host reads a setting from the environment but the adapters that are named as doing so,
/// and a file under a ring's Features/&lt;Slice&gt;/ declares that slice's namespace.
/// </summary>
public sealed partial class RingTests
{
    private static readonly Assembly Domain = typeof(TrackerCsvText).Assembly;
    private static readonly Assembly Application = typeof(IMergeStore).Assembly;
    private static readonly Assembly Infrastructure = typeof(ShareMergeStore).Assembly;

    private static IReadOnlyList<string> JobHuntReferences(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(name => name.Name!).Where(name => name.StartsWith("JobHunt.", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

    [Fact]
    public void Each_ring_leans_only_inward()
    {
        Assert.Equal("JobHunt.Domain", Domain.GetName().Name);
        Assert.Empty(JobHuntReferences(Domain));
        Assert.Equal(new[] { "JobHunt.Domain" }, JobHuntReferences(Application));
        Assert.DoesNotContain("JobHunt.Functions", JobHuntReferences(Infrastructure));
        Assert.Contains("JobHunt.Application", JobHuntReferences(Infrastructure));
    }

    [Fact]
    public void Only_Infrastructure_reaches_the_share_or_the_network()
    {
        foreach (var ring in new[] { Domain, Application })
        {
            var names = ring.GetReferencedAssemblies().Select(name => name.Name!).ToList();
            Assert.DoesNotContain(names, name => name.StartsWith("Azure.", StringComparison.Ordinal));
            Assert.DoesNotContain("System.Net.Http", names);
            Assert.DoesNotContain("Microsoft.Extensions.Http", names);
            Assert.DoesNotContain("Microsoft.Data.SqlClient", names);
        }
        Assert.Contains("Microsoft.Data.SqlClient", Infrastructure.GetReferencedAssemblies().Select(name => name.Name!));
        // No type in the two inner rings names a client, a handler or a client factory: tests/rules/SocketRule.cs,
        // the same body the site's suite runs over its host.
        var doors = TheYard.Rules.SocketRule.Opens(Domain).Concat(TheYard.Rules.SocketRule.Opens(Application)).ToList();
        Assert.True(doors.Count == 0, string.Join(Environment.NewLine, doors));
    }

    [Fact]
    public void Infrastructure_reads_the_environment_only_in_these_files()
    {
        // The test above holds Domain and Application to no environment read. Infrastructure still reads it in
        // four files until stage B of ADR-117 hands each host its settings record; until then the files and their
        // counts are named here, so a fifth file, or a new read in one of these, fails. When stage B lands the list
        // empties and this becomes the rule.
        var named = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["functions/JobHunt.Infrastructure/Shared/Share/ShareSnapshot.cs"] = 1,
            ["functions/JobHunt.Infrastructure/Shared/GitHub/Dispatch.cs"] = 3,
            ["functions/JobHunt.Infrastructure/Shared/Google/GoogleCredentials.cs"] = 3,
            ["functions/JobHunt.Infrastructure/Features/Sheet/GoogleSheetGrid.cs"] = 1,
        };
        var reads = Directory.EnumerateFiles(Path.Combine(Root(), "functions", "JobHunt.Infrastructure"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => (path: Path.GetRelativePath(Root(), path).Replace('\\', '/'), count: Regex.Matches(File.ReadAllText(path), "GetEnvironmentVariable").Count))
            .Where(file => file.count > 0)
            .ToDictionary(file => file.path, file => file.count, StringComparer.Ordinal);
        Assert.Equal(named.OrderBy(pair => pair.Key, StringComparer.Ordinal), reads.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void Nothing_in_Domain_or_Application_reads_the_environment()
    {
        var reads = new[] { "JobHunt.Domain", "JobHunt.Application" }
            .SelectMany(ring => Directory.EnumerateFiles(Path.Combine(Root(), "functions", ring), "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path) && File.ReadAllText(path).Contains("GetEnvironmentVariable", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(Root(), path).Replace('\\', '/'))
            .ToList();
        Assert.True(reads.Count == 0, "reads the environment below the adapters: " + string.Join(", ", reads));
    }

    [Fact]
    public void Nothing_in_Domain_or_Application_writes_a_mail_search()
    {
        // A search language is an adapter's concern: Domain keeps what a board is (its senders and the subject
        // phrases its alerts carry) as data, Application carries them in a MailSearch, and only the Gmail
        // adapter composes Gmail's syntax. No code line under Domain or Application writes one of its words.
        var words = new[] { "from:", "subject:", "in:inbox", "newer_than:" };
        var found = new[] { "JobHunt.Domain", "JobHunt.Application" }
            .SelectMany(ring => Directory.EnumerateFiles(Path.Combine(Root(), "functions", ring), "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => File.ReadAllLines(path)
                .Select((line, index) => (path, number: index + 1, code: CodeOf(line)))
                .Where(line => words.Any(word => line.code.Contains(word, StringComparison.Ordinal))))
            .Select(line => $"{Path.GetRelativePath(Root(), line.path).Replace('\\', '/')}:{line.number} writes a mail search")
            .ToList();
        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
    }

    /// <summary>A line without its comment: everything before the first // that is not inside a string.</summary>
    private static string CodeOf(string line)
    {
        bool quoted = false;
        for (int i = 0; i + 1 < line.Length; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                quoted = !quoted;
            }
            else if (!quoted && line[i] == '/' && line[i + 1] == '/')
            {
                return line[..i];
            }
        }
        return line;
    }

    [Fact]
    public void A_file_under_a_ring_s_slice_declares_that_slice_s_namespace()
    {
        // The body is tests/rules/NamespaceRule.cs, the one the site's suite runs over its rings.
        var wrong = new[] { "Domain", "Application", "Infrastructure" }
            .SelectMany(ring => TheYard.Rules.NamespaceRule.NotTheirFolder(Path.Combine(Root(), "functions", "JobHunt." + ring), "JobHunt." + ring, Root()))
            .ToList();
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void No_production_source_file_runs_past_three_hundred_lines()
    {
        // The site's 300-line rule over the job hunt's code too: tests/rules/LengthRule.cs, one body for both suites.
        var files = Directory.EnumerateFiles(Path.Combine(Root(), "functions"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path) && !path.Contains($"{Path.DirectorySeparatorChar}jobhunt.Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        var tooLong = TheYard.Rules.LengthRule.TooLong(files, Root());
        Assert.True(tooLong.Count == 0, string.Join(Environment.NewLine, tooLong));
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "functions", "JobHunt.Domain")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("functions/JobHunt.Domain not found above " + AppContext.BaseDirectory);
    }
}
