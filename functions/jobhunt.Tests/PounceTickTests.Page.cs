using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The 90-day page for a caught role: beside the wrapper that starts the first touch, the hand-off queues one Sonnet item
/// that builds the role's page with gen90.py for the leader the feed named, after that first touch and never ahead of
/// one. The page is built and logged and never attached, whatever POUNCE_RUN says, and "off" queues no page either. A
/// page that fails a gen90 check, or a company the red flag scan flags, gets no page and a log line that says which and why.
/// </summary>
public sealed partial class PounceTickTests
{
    private const string PageItem = "dispatch/ready/20-pounce90-12786.md";

    [Fact]
    public async Task A_passed_role_also_gets_one_page_item_on_Sonnet_that_sorts_after_its_first_touch_and_holds_nothing()
    {
        using var scratch = new Scratch();
        await PounceHandoffs.WriteAsync(scratch.Store, [(Caught(), "Staff Software Engineer - Web", "https://job-boards.greenhouse.io/zephyr/jobs/1")], PounceHandoff.Dry, CancellationToken.None);

        Assert.Equal(["10-pounce-12786.md", "20-pounce90-12786.md"], Directory.GetFiles(Path.Combine(scratch.Root, "dispatch", "ready")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        string item = scratch.Text(PageItem);
        var header = item.Split('\n').TakeWhile(line => line.Contains(':', StringComparison.Ordinal) && !line.StartsWith('#')).ToList();
        Assert.Equal("Lane: pounce90-12786", header[0]);
        Assert.Contains("Model: claude-sonnet-5-5", header);
        Assert.Contains("Holds:", header);
        Assert.Contains("Needs: chrome, jdrive", header);
        Assert.Contains("Scope: jobhunt", header);
        Assert.Contains(@"Done_note: J:\nightly\pounce\20261111-160500Z-12786-90day-DONE.md", header);
        Assert.DoesNotContain(header, line => line.Contains("opus", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Not_before:", StringComparison.Ordinal));
        Assert.DoesNotContain(header.Where(line => !line.StartsWith("Needs:", StringComparison.Ordinal)), line => Holding().IsMatch(line));
        Assert.DoesNotContain((char)0x2014, item);
        Assert.DoesNotContain("view=admin", item, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_page_item_writes_the_spec_and_the_red_flag_scan_first_runs_gen90_for_the_feed_s_leader_and_attaches_nothing()
    {
        using var scratch = new Scratch();
        await PounceHandoffs.WriteAsync(scratch.Store, [(Caught(), "Staff Software Engineer - Web", "https://job-boards.greenhouse.io/zephyr/jobs/1")], PounceHandoff.Dry, CancellationToken.None);
        string item = scratch.Text(PageItem);

        int queue = item.IndexOf("QUEUE-pounce-*-12786.md", StringComparison.Ordinal);
        int scan = item.IndexOf("redflag_scan", StringComparison.Ordinal);
        int spec = item.IndexOf("/mnt/j/nightly/pounce/20261111-160500Z-12786-90day/spec.json", StringComparison.Ordinal);
        int gen90 = item.IndexOf("python3 -I /mnt/j/mentor/90day-template/gen90.py /mnt/j/nightly/pounce/20261111-160500Z-12786-90day/spec.json /mnt/j/nightly/pounce/20261111-160500Z-12786-90day/page --person", StringComparison.Ordinal);
        Assert.True(queue > 0 && scan > queue && spec > 0 && gen90 > scan && gen90 > spec, "the leader, then the spec with its red flag scan, then gen90");
        Assert.Contains("LANE=pounce90-12786", item, StringComparison.Ordinal);
        Assert.Contains("ls /mnt/j/dispatch/ready/10-pounce-*.md", item, StringComparison.Ordinal);
        Assert.Contains("FOLLOW-ON:", item, StringComparison.Ordinal);
        Assert.Contains("/mnt/j/nightly/pounce/pages.log", item, StringComparison.Ordinal);
        Assert.Contains("NO PAGE red flag:", item, StringComparison.Ordinal);
        Assert.Contains("NO PAGE gen90:", item, StringComparison.Ordinal);
        Assert.Contains("NO PAGE no leader:", item, StringComparison.Ordinal);
        Assert.Contains("never attached", item, StringComparison.Ordinal);
        Assert.Contains("Never run gen90.py a second time", item, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_page_stays_in_shadow_when_the_switch_is_live_and_off_queues_no_page()
    {
        using var dry = new Scratch();
        await PounceHandoffs.WriteAsync(dry.Store, [(Caught(), "Staff Engineer", "https://x/1")], PounceHandoff.Dry, CancellationToken.None);
        using var live = new Scratch();
        await PounceHandoffs.WriteAsync(live.Store, [(Caught(), "Staff Engineer", "https://x/1")], PounceHandoff.Live, CancellationToken.None);
        Assert.Equal(dry.Text(PageItem), live.Text(PageItem));

        using var off = new Scratch();
        await PounceHandoffs.WriteAsync(off.Store, [(Caught(), "Staff Engineer", "https://x/1")], PounceHandoff.Off, CancellationToken.None);
        Assert.False(off.Has(PageItem));
    }
}
