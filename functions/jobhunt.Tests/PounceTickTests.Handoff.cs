using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The hand-off (pounce1111 step 4): each role a tick passed gets one JSON file under nightly/pounce/ and one dispatcher
/// item in dispatch/ready/ that starts the 6 AM program once for that role, a dry run unless POUNCE_RUN says live, and
/// the switch "off" writes neither. The item is a wrapper that holds nothing, and the rows the dispatcher builds from
/// its header never name the browser or the network, so it cannot hold the program it starts.
/// </summary>
public sealed partial class PounceTickTests
{
    private const string HandoffJson = """{"key":12786,"company":"Zephyr Labs","title":"Staff Software Engineer - Web","url":"https://job-boards.greenhouse.io/zephyr/jobs/1","band":"PASS","live_at":"2026-11-11T15:25:00+00:00","live_known":true,"budget":false,"passed_at":"2026-11-11T16:05:00+00:00"}""";

    private static PounceRole Caught(bool budget = false) =>
        new(12786, "Zephyr Labs", Now.AddMinutes(-40), true, Now.AddMinutes(-35), Now, null, "PASS", budget);

    [Fact]
    public async Task A_passed_role_gets_a_hand_off_and_one_ready_item_that_starts_one_dry_run()
    {
        using var scratch = new Scratch();
        int written = await PounceHandoffs.WriteAsync(scratch.Store, [(Caught(), "Staff Software Engineer - Web", "https://job-boards.greenhouse.io/zephyr/jobs/1")], PounceHandoff.Dry, CancellationToken.None);

        Assert.Equal(1, written);
        Assert.Equal(HandoffJson, scratch.Text("nightly/pounce/20261111-160500Z-12786.json"));
        string item = scratch.Text("dispatch/ready/10-pounce-12786.md");
        var header = item.Split('\n').TakeWhile(line => line.Contains(':', StringComparison.Ordinal) && !line.StartsWith('#')).ToList();
        Assert.Equal("Lane: pounce-12786", header[0]);
        Assert.Contains("Holds:", header);
        Assert.Contains(@"Done_note: J:\nightly\pounce\20261111-160500Z-12786-DONE.md", header);
        Assert.DoesNotContain(header.Where(line => !line.StartsWith("Needs:", StringComparison.Ordinal)), line => Holding().IsMatch(line));
        Assert.Contains("systemd-run --user --unit=pounce-12786 --collect", item, StringComparison.Ordinal);
        Assert.Contains("run --slot pounce-12786 --role nightly/pounce/20261111-160500Z-12786.json --dry-run\n", item, StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(Path.Combine(scratch.Root, "dispatch", "ready"), "10-*"));
        Assert.DoesNotContain((char)0x2014, item);
    }

    [Fact]
    public async Task Live_drops_the_dry_run_flag_off_writes_nothing_and_the_switch_reads_three_ways()
    {
        using var scratch = new Scratch();
        await PounceHandoffs.WriteAsync(scratch.Store, [(Caught(budget: true), "Staff Engineer", "https://x/1")], PounceHandoff.Live, CancellationToken.None);
        string item = scratch.Text("dispatch/ready/10-pounce-12786.md");
        Assert.Contains("--role nightly/pounce/20261111-160500Z-12786.json\n", item, StringComparison.Ordinal);
        Assert.Contains("budget flag", item, StringComparison.Ordinal);
        Assert.Contains("\"budget\":true", scratch.Text("nightly/pounce/20261111-160500Z-12786.json"), StringComparison.Ordinal);

        using var off = new Scratch();
        Assert.Equal(0, await PounceHandoffs.WriteAsync(off.Store, [(Caught(), "Staff Engineer", "https://x/1")], PounceHandoff.Off, CancellationToken.None));
        Assert.False(off.Has("nightly/pounce/20261111-160500Z-12786.json"));

        Assert.Equal((PounceHandoff.Dry, PounceHandoff.Off, PounceHandoff.Live, PounceHandoff.Dry), (PounceHandoff.Mode(null), PounceHandoff.Mode(" Off "), PounceHandoff.Mode("LIVE"), PounceHandoff.Mode("on")));
    }

    [Fact]
    public async Task The_tick_carries_each_passed_role_s_address_and_a_share_that_does_not_answer_fails_no_tick()
    {
        using var scratch = new Scratch();
        var postings = new Postings();
        postings.Add(1, Role(), Now.AddMinutes(-20), Now.AddMinutes(-31), "C# on .NET and Azure");
        var tick = await new PounceTick(scratch.Store, postings, new Tracker(), new Book(postings), new Fixed(Now)).RunAsync(false, CancellationToken.None);
        Assert.Equal("https://job-boards.greenhouse.io/acme/jobs/1", Assert.Single(tick.Passed).Url);

        var run = new RunLine("Pounce", Now);
        await PounceTimer.HandOffAsync(new Down(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, run, tick, PounceHandoff.Dry, CancellationToken.None);
        Assert.Equal(("success", 1L), (run.Outcome, run.Counts["handoff_failed"]));
        var handed = new RunLine("Pounce", Now);
        await PounceTimer.HandOffAsync(scratch.Store, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, handed, tick, PounceHandoff.Dry, CancellationToken.None);
        Assert.Equal(1L, handed.Counts["handed"]);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"linkedin|connection request|invite|claude in chrome|chrome", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex Holding();
}
