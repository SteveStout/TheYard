using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The plan card's live source (ADR: Live dials where the source allows, the addendum on the plan's /proc fork): memory
/// is what the kernel says is not available of what it has, the processor is the busy share of every core between two
/// reads (none on the first), the source says why when there is no /proc, and the parsers read a real file's shape.
/// </summary>
public sealed class ProcLiveSourceTests : IDisposable
{
    private static readonly DateTimeOffset Nine = new(2026, 11, 7, 15, 0, 0, TimeSpan.Zero);

    private readonly string _root = Directory.CreateTempSubdirectory("proc-fixture-").FullName;

    private const string Meminfo = "MemTotal:        3963904 kB\nMemFree:          512000 kB\nMemAvailable:    2378342 kB\nBuffers:           10240 kB\n";

    private void Stat(long user, long idle) =>
        File.WriteAllText(Path.Combine(_root, "stat"), $"cpu  {user} 0 100 {idle} 50 0 10 0 0 0\ncpu0 1 0 1 1 0 0 0 0 0 0\nintr 1\n");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Memory_is_what_is_not_available_and_the_processor_is_the_busy_share_since_the_last_read()
    {
        File.WriteAllText(Path.Combine(_root, "meminfo"), Meminfo);
        Stat(user: 1000, idle: 9000);
        var source = new ProcLiveSource(_root);

        var first = await source.SampleAsync(Nine, CancellationToken.None);
        Stat(user: 1300, idle: 9700);
        var second = await source.SampleAsync(Nine.AddSeconds(2), CancellationToken.None);

        Assert.Equal(("plan", 2, "this instance, /proc, 2 s"), (source.Key, source.GrainSeconds, source.Source));
        Assert.Null(source.Absent);
        Assert.Equal(40.0, first!.Values["memory"]);
        Assert.Null(first.Values["cpu"]);
        Assert.Equal(3.78, first.Values["memory_gb"]);
        Assert.Equal(30.0, second!.Values["cpu"]);
        Assert.Equal(Nine.AddSeconds(2), second.At);
    }

    [Fact]
    public void Without_proc_the_source_says_so_and_the_parsers_read_nothing_as_nothing()
    {
        var source = new ProcLiveSource(Path.Combine(_root, "absent"));

        Assert.Equal("this process runs where there is no /proc to read", source.Absent);
        Assert.Equal((0L, 0L), ProcLiveSource.Memory(""));
        Assert.Equal((0L, 0L), ProcLiveSource.CpuTimes("intr 1\n"));
        Assert.Equal((3963904L, 2378342L), ProcLiveSource.Memory(Meminfo));
        Assert.Equal((1110L, 10160L), ProcLiveSource.CpuTimes("cpu  1000 0 100 9000 50 0 10 0 0 0\n"));
    }

    [Fact]
    public async Task On_this_box_the_source_reads_its_own_proc()
    {
        if (!File.Exists("/proc/meminfo"))
        {
            return;
        }
        var source = new ProcLiveSource();

        var sample = await source.SampleAsync(Nine, CancellationToken.None);

        Assert.InRange(sample!.Values["memory"]!.Value, 0, 100);
        Assert.True(sample.Values["memory_gb"] > 0);
    }
}
