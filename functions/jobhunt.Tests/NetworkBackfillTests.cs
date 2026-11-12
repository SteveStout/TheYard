using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// A backfill that does not finish picks up where it stopped: the resume point is one second past the oldest thread
/// it read, a whole read ends it, a read that got no further keeps its point when cut and ends when only capped, the
/// next pass asks Gmail for the window before that point beside its own two days, and a Monday flipped to `on` after
/// its shadow pass runs once more outside the 6 o'clock hour.
/// </summary>
public sealed class NetworkBackfillTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("netlog-backfill-").FullName;

    private static readonly DateTimeOffset Monday = new(2026, 11, 9, 12, 5, 0, TimeSpan.Zero);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static ThreadSummary Thread(string id, string from, DateTimeOffset theirs, DateTimeOffset? steve) =>
        new(id, "Platform role", [new ThreadMessage(from, theirs, false, false, "", "stevenstout11@gmail.com", $"<{id}@x>"), .. steve is { } s ? new[] { new ThreadMessage("Steven Stout <stevenstout11@gmail.com>", s, true, false) } : []]);

    private (NetworkLogPass Pass, NetworkFakes.Rows Rows, NetworkFakes.Gmail Gmail) Build()
    {
        var rows = new NetworkFakes.Rows();
        var gmail = new NetworkFakes.Gmail();
        return (new NetworkLogPass(rows, gmail, new NoDraftWriter(), new FolderMergeStore(_root), new NetworkFakes.FixedTime(Monday)), rows, gmail);
    }

    private string Point() => File.ReadAllText(Path.Combine(_root, BackfillRule.Path));

    [Fact]
    public void The_point_reads_back_and_done_reads_as_none()
    {
        var at = new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(at, BackfillRule.Read(BackfillRule.Text(at, Monday)));
        Assert.StartsWith("done ", BackfillRule.Text(null, Monday), StringComparison.Ordinal);
        Assert.Null(BackfillRule.Read(BackfillRule.Text(null, Monday)));
        Assert.Null(BackfillRule.Read(""));
        Assert.Null(BackfillRule.Read("not a time"));
    }

    [Fact]
    public void The_next_point_moves_back_ends_when_whole_and_never_loops()
    {
        var read = new[] { Thread("a", "p@acme.com", Monday.AddDays(-3), Monday.AddDays(-2)), Thread("b", "q@acme.com", Monday.AddDays(-30), null) };
        var before = Monday.AddDays(-20);
        Assert.Null(BackfillRule.Next(cut: false, capped: false, read, before));
        Assert.Equal(Monday.AddDays(-30).AddSeconds(1), BackfillRule.Next(cut: true, capped: false, read, null));
        Assert.Equal(Monday.AddDays(-30).AddSeconds(1), BackfillRule.Next(cut: false, capped: true, read, before));
        Assert.Equal(Monday.AddDays(-40), BackfillRule.Next(cut: true, capped: false, read, Monday.AddDays(-40)));
        Assert.Null(BackfillRule.Next(cut: false, capped: true, read, Monday.AddDays(-40)));
        Assert.Equal(before, BackfillRule.Next(cut: true, capped: false, [], before));
    }

    [Fact]
    public async Task A_first_pass_at_the_cap_leaves_its_point_for_the_next()
    {
        var (pass, _, gmail) = Build();
        for (int i = 0; i < NetworkLogPass.MaxThreads; i++)
        {
            gmail.Threads[$"t{i}"] = Thread($"t{i}", $"Pat Lee <pat{i}@acme.com>", Monday.AddMinutes(-i), null);
        }
        await pass.RunAsync(NetworkMode.Shadow, new HashSet<string>(), CancellationToken.None);
        Assert.Equal(BackfillRule.Text(Monday.AddMinutes(-(NetworkLogPass.MaxThreads - 1)).AddSeconds(1), Monday), Point());
    }

    [Fact]
    public async Task The_next_pass_reads_before_the_point_beside_its_two_days_and_says_done()
    {
        var (pass, rows, gmail) = Build();
        rows.Store["x@acme.com"] = new Conversation { PersonKey = "x@acme.com", FirstName = "X", FullName = "x", Email = "x@acme.com", Source = "gmail", ThreadIds = ["t9"], FirstTwoWayAt = Monday.AddDays(-3) };
        var before = Monday.AddDays(-23);
        Directory.CreateDirectory(Path.Combine(_root, NetworkFiles.Folder));
        await File.WriteAllTextAsync(Path.Combine(_root, BackfillRule.Path), BackfillRule.Text(before, Monday));
        gmail.Threads["new"] = Thread("new", "Sam Ortiz <sam@globex.com>", Monday.AddDays(-1), Monday.AddHours(-20));
        gmail.Threads["old"] = Thread("old", "Pat Lee <pat@acme.com>", Monday.AddDays(-30), Monday.AddDays(-29));

        var result = await pass.RunAsync(NetworkMode.Shadow, new HashSet<string>(), CancellationToken.None);

        Assert.Equal([2, 120], gmail.Searches.Select(s => s.NewerThanDays));
        Assert.Equal(before, gmail.Searches[1].Before);
        Assert.Null(gmail.Searches[0].Before);
        Assert.Contains(" threads=2 ", result.Line, StringComparison.Ordinal);
        Assert.Contains(" resumed=1 ", result.Line, StringComparison.Ordinal);
        Assert.Contains("pat@acme.com", rows.Store.Keys);
        Assert.StartsWith("done ", Point(), StringComparison.Ordinal);
        Assert.Null(BackfillRule.Read(Point()));
    }

    [Fact]
    public async Task A_resumed_backfill_reads_one_slice_a_morning_so_the_pass_stays_short()
    {
        var (pass, rows, gmail) = Build();
        rows.Store["x@acme.com"] = new Conversation { PersonKey = "x@acme.com", FirstName = "X", FullName = "x", Email = "x@acme.com", Source = "gmail", ThreadIds = ["t9"], FirstTwoWayAt = Monday.AddDays(-3) };
        var before = Monday.AddDays(-23);
        Directory.CreateDirectory(Path.Combine(_root, NetworkFiles.Folder));
        await File.WriteAllTextAsync(Path.Combine(_root, BackfillRule.Path), BackfillRule.Text(before, Monday));
        for (int i = 0; i < 1500; i++)
        {
            gmail.Threads[$"o{i}"] = Thread($"o{i}", $"Pat Lee <pat{i}@acme.com>", before.AddHours(-1 - i), null);
        }

        var result = await pass.RunAsync(NetworkMode.Shadow, new HashSet<string>(), CancellationToken.None);

        Assert.Equal(100, gmail.Searches[1].Max);
        Assert.Contains(" resumed=100 ", result.Line, StringComparison.Ordinal);
        Assert.Equal(0, result.Cut);
        Assert.Equal(before.AddHours(-100).AddSeconds(1), BackfillRule.Read(Point()));
    }

    [Fact]
    public async Task A_daily_pass_with_no_point_asks_for_two_days_only()
    {
        var (pass, rows, gmail) = Build();
        rows.Store["x@acme.com"] = new Conversation { PersonKey = "x@acme.com", FirstName = "X", FullName = "x", Email = "x@acme.com", Source = "gmail", ThreadIds = ["t9"], FirstTwoWayAt = Monday.AddDays(-3) };
        var result = await pass.RunAsync(NetworkMode.Shadow, new HashSet<string>(), CancellationToken.None);
        Assert.Single(gmail.Searches);
        Assert.DoesNotContain("resumed=", result.Line, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_root, BackfillRule.Path)));
    }

    [Fact]
    public void A_Monday_flipped_after_its_shadow_pass_runs_once_more_off_the_hour()
    {
        var day = new DateOnly(2026, 11, 9);
        Assert.True(NetworkLogTimer.Flipped(new PassRow(day, "shadow", Monday.AddHours(-9), Monday.AddHours(-9)), "on"));
        Assert.False(NetworkLogTimer.Flipped(new PassRow(day, "on", Monday.AddHours(-1), Monday.AddHours(-1)), "on"));
        Assert.False(NetworkLogTimer.Flipped(new PassRow(day, "shadow", Monday.AddHours(-9), Monday.AddHours(-9)), "shadow"));
        Assert.False(NetworkLogTimer.Flipped(new PassRow(day, "shadow", Monday.AddMinutes(-30), null), "on"));
        Assert.False(NetworkLogTimer.Flipped(null, "on"));
    }
}
