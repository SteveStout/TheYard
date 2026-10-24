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
    public void A_file_under_a_ring_s_slice_declares_that_slice_s_namespace()
    {
        var wrong = new List<string>();
        foreach (string ring in new[] { "Domain", "Application", "Infrastructure" })
        {
            string root = Path.Combine(Root(), "functions", "JobHunt." + ring);
            foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(path => !IsBuildOutput(path)))
            {
                var parts = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);
                string expected = parts.Length > 2 && parts[0] == "Features" ? $"JobHunt.{ring}.Features.{parts[1]}" : $"JobHunt.{ring}";
                string declared = Namespace().Match(File.ReadAllText(path)).Groups[1].Value;
                if (declared != expected && !(path.EndsWith("NightlySlots.cs", StringComparison.Ordinal)))
                {
                    wrong.Add($"{Path.GetRelativePath(Root(), path).Replace('\\', '/')} declares {declared}, its folder says {expected}");
                }
            }
        }
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
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

    [GeneratedRegex(@"(?m)^namespace\s+([\w.]+)")]
    private static partial Regex Namespace();
}
