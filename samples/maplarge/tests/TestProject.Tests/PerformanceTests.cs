using System.Diagnostics;
using TestProject.Application;
using TestProject.Data;
using TestProject.Domain;
using TestProject.Infrastructure;
using Xunit.Abstractions;

namespace TestProject.Tests;

/// <summary>
/// Performance, measured rather than claimed (ADR-008). A tree of 10,000 files
/// in 100 folders is generated once per run under temp, and the use cases are
/// timed over the real disk adapter. The bars are loose on purpose (a laptop
/// with a virus scanner is the target machine); the numbers printed are what
/// the record quotes.
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

        // Warm the operating system's directory cache once, the way a second visitor finds it.
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
