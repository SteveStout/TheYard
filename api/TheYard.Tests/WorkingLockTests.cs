using System.Text.RegularExpressions;

namespace TheYard.Tests;

// #region working-lock-tests
/// <summary>
/// The working lock is one, registered once (ADR: One working lock for the whole site, after half
/// a second): the shell mounts it and nothing else draws it, the wait is half a second in the one
/// store, and the three request files go through that store. The behaviour itself is held by
/// src/lib/working.test.ts and tests/e2e/working.spec.ts; this holds the shape, so a second lock or
/// a fetch that skips the store fails here in seconds.
/// </summary>
public sealed class WorkingLockTests
{
    private static string Source(params string[] parts) => File.ReadAllText(Path.Combine([Repo.Root(), .. parts]));

    [Fact]
    public void The_lock_is_mounted_once_in_the_shell_and_nowhere_else()
    {
        var mounts = Directory.EnumerateFiles(Path.Combine(Repo.Root(), "src"), "*.tsx", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".test.tsx", StringComparison.Ordinal))
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"<WorkingLock\s*/>"))
            .Select(path => Path.GetRelativePath(Repo.Root(), path).Replace('\\', '/'))
            .ToList();
        Assert.Equal(["src/app/TheYard.tsx"], mounts);
    }

    [Fact]
    public void The_wait_is_half_a_second_in_the_one_store_and_the_request_files_go_through_it()
    {
        Assert.Matches(@"export const LOCK_AFTER_MS = 500;", Source("src", "lib", "working.ts"));
        foreach (string file in new[] { "data.ts", "auth.ts", "prefetch.ts" })
        {
            string source = Source("src", "lib", file);
            Assert.Matches(@"import \{ counted\b[^}]*\} from './working';", source);
            // Every fetch in the file sits inside counted(...): none reaches the network unseen.
            int fetches = Regex.Matches(source, @"\bfetch\(").Count;
            int wrapped = Regex.Matches(source, @"counted\(\s*\(\)\s*=>\s*(?:\n\s*)?fetch\(").Count;
            Assert.True(file == "prefetch.ts" || fetches == wrapped, $"{file}: {fetches} fetches, {wrapped} inside counted");
        }
    }
    /// <summary>
    /// The fetches that stay outside the store, each a read nobody clicked for, by file and line. A new
    /// raw fetch anywhere under src fails here until it goes through counted or is named below with its
    /// reason; a named line that moves fails too, so the list never names a line that is not a fetch.
    /// </summary>
    private static readonly (string File, int Line, string Why)[] Background =
    [
        ("src/app/App.tsx", 68, "the tagged-open report, sent once on load"),
        ("src/components/admin/AdminPanel/useAdminReads.ts", 105, "the strip's activity read on the tab's clock"),
        ("src/components/admin/PagesCard/PagesCard.tsx", 49, "the card's own read of the last sweep"),
        ("src/components/admin/ProofCard/ProofCard.tsx", 214, "the three second poll of a running proof"),
        ("src/components/landing/Landing/Landing.tsx", 73, "the landing page's health read on load"),
        ("src/components/landing/Landing/Landing.tsx", 81, "the landing page's test summary on load"),
        ("src/components/landing/PulseBand/PulseBand.tsx", 46, "the pulse band's read on its own clock"),
        ("src/components/shared/ErrorBoundary/ErrorBoundary.tsx", 101, "the error report, which must never lock"),
        ("src/components/shared/Recommendations/Recommendations.tsx", 39, "the column's read on load"),
    ];

    [Fact]
    public void Every_fetch_in_src_goes_through_the_store_or_is_a_named_background_read()
    {
        string root = Repo.Root();
        var raw = new List<string>();
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(path => Regex.IsMatch(path, @"\.tsx?$") && !Regex.IsMatch(path, @"\.test\.tsx?$")))
        {
            string text = File.ReadAllText(path);
            foreach (Match fetch in Regex.Matches(text, @"\bfetch\("))
            {
                string before = Regex.Replace(text[Math.Max(0, fetch.Index - 60)..fetch.Index], @"\s+", "");
                if (before.EndsWith("counted(()=>", StringComparison.Ordinal))
                {
                    continue;
                }
                int line = text[..fetch.Index].Count(c => c == '\n') + 1;
                raw.Add($"{Path.GetRelativePath(root, path).Replace('\\', '/')}:{line}");
            }
        }
        Assert.Equal(Background.Select(b => $"{b.File}:{b.Line}").Order(StringComparer.Ordinal), raw.Order(StringComparer.Ordinal));
    }
}
// #endregion working-lock-tests
