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
}
// #endregion working-lock-tests
