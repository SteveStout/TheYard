using System.Diagnostics;
using TestProject.Application;
using TestProject.Data;
using TestProject.Domain;
using TestProject.Infrastructure;
using Xunit.Abstractions;

namespace TestProject.Tests;

/// <summary>
/// Measures browse and search speed on a real disk instead of assuming it. The test writes
/// 10,000 files in 100 folders under temp, then times a folder browse, a capped search, a
/// whole-tree search and a root browse through the class that reads real files. The time limits
/// are loose on purpose, so a slow laptop running a virus scanner still passes. The timings the
/// test prints are the figures quoted in docs/ADR-008-performance-measured.md.
/// </summary>
public sealed class PerformanceTests(ITestOutputHelper output) : IDisposable
{
    private const int Folders = 100;
    private const int FilesPerFolder = 100;
    private readonly TempHome _home = new();

    public void Dispose()
    {
        _home.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Browse_search_and_a_capped_search_over_ten_thousand_files()
    {
        Stopwatch generated = Stopwatch.StartNew();
        for (int folder = 0; folder < Folders; folder++)
        {
            string path = _home.Folder($"folder-{folder:D3}");
            for (int file = 0; file < FilesPerFolder; file++)
            {
                File.WriteAllText(Path.Combine(path, $"file-{folder:D3}-{file:D3}.txt"), "x");
            }
        }
        generated.Stop();

        var browser = new FileBrowser(new HomePath(_home.Root), new PhysicalFileStore(), new FilesOptions());

        // Browse once before timing to warm the OS directory cache, so the timings match a
        // repeat visit rather than the first read of a cold disk.
        browser.Browse("folder-000");

        long browseMs = Time(() => browser.Browse("folder-042"), out int browsed);
        long cappedMs = Time(() => browser.Search("", "*.txt", 200), out int capped);
        long fullMs = Time(() => browser.Search("", "file-099-099", null), out int found);
        long rootMs = Time(() => browser.Browse(""), out int folders);

        output.WriteLine($"generated {Folders * FilesPerFolder:N0} files in {generated.ElapsedMilliseconds} ms");
        output.WriteLine($"browse one folder of {browsed} files: {browseMs} ms");
        output.WriteLine($"browse the root of {folders} folders: {rootMs} ms");
        output.WriteLine($"search capped at {capped} matches: {cappedMs} ms");
        output.WriteLine($"search the whole tree for one name ({found} match): {fullMs} ms");

        Assert.Equal(FilesPerFolder, browsed);
        Assert.Equal(200, capped);
        Assert.Equal(1, found);
        Assert.True(browseMs < 250, $"browsing one folder took {browseMs} ms");
        Assert.True(cappedMs < 500, $"a capped search took {cappedMs} ms");
        Assert.True(fullMs < 3000, $"a full-tree search took {fullMs} ms");
    }

    private static long Time(Func<object> act, out int count)
    {
        Stopwatch watch = Stopwatch.StartNew();
        object result = act();
        watch.Stop();
        count = result switch
        {
            Listing listing => listing.Folders.Count + listing.Files.Count,
            SearchResult search => search.Folders.Count + search.Files.Count,
            _ => 0,
        };
        return watch.ElapsedMilliseconds;
    }
}
