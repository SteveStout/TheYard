using System.Reflection;
using Xunit;

namespace JobHunt.Functions.Tests;

// #region function-slice-tests
/// <summary>
/// The job hunt's Function cut into vertical slices (ADR-107, the addendum of 1.0.4.75): each ring keeps a slice under
/// Features/&lt;Slice&gt;/ (Ats, Campaign, Card, Leads, Merge, Sheet, Triage) and what more than one slice uses under
/// Shared/&lt;Concern&gt;/. A slice uses only its own types and the shared ones in any ring, and shared code uses no
/// slice: tests/rules/SliceRule.cs, read from the compiled rings.
/// </summary>
public sealed class SliceTests
{
    private static readonly Assembly[] Rings = [typeof(TrackerCsvText).Assembly, typeof(IMergeStore).Assembly, typeof(ShareMergeStore).Assembly];

    [Fact]
    public void A_slice_uses_no_other_slice_and_shared_code_uses_no_slice_in_any_ring()
    {
        var wrong = TheYard.Rules.SliceRule.Crossings(Rings);
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Every_ring_file_sits_in_a_slice_or_a_shared_concern()
    {
        // As in the site's inner rings since 1.0.4.46: nothing at a ring's root, so a reader finds a type by its feature.
        string root = Path.Combine(Repo(), "functions");
        var loose = new[] { "JobHunt.Domain", "JobHunt.Application", "JobHunt.Infrastructure" }
            .SelectMany(ring => Directory.EnumerateFiles(Path.Combine(root, ring), "*.cs", SearchOption.TopDirectoryOnly))
            .Select(path => Path.GetRelativePath(Repo(), path).Replace('\\', '/'))
            .ToList();
        Assert.True(loose.Count == 0, "at a ring's root: " + string.Join(", ", loose));
    }

    private static string Repo()
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
// #endregion function-slice-tests
