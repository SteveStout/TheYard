using JobHunt.Application.Features.Watch;
using JobHunt.Domain.Features.Watch;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// One watch tick over a share born and thrown away with the test (FolderMergeStore on a temp folder, the merger tests'
/// own way) and a settings reader that is a dictionary: no live share, no Function setting, no Gmail (RULE 79).
/// </summary>
public sealed class WatchPassTests
{
    private static readonly DateTimeOffset NineThirteen = new(2026, 11, 20, 15, 13, 0, TimeSpan.Zero);

    private const string Switches = """
        ## Switches

        | Name | Live value | In infra/jobhunt.bicep | Who | Restore |
        |---|---|---|---|---|
        | MERGER | on | yes | the template | yes |
        | POUNCE_RUN | live since 2026-11-19 14:48 CST | no | golive1119 | yes |

        ## Secrets (name and "set" only; never a value)

        | Name | Live | In the template | Expiry | What |
        |---|---|---|---|---|
        | GMAIL_REFRESH_TOKEN | set | no | none | Gmail |
        """;

    private sealed class At(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static (Scratch Share, Func<string, string?> Setting) World(Dictionary<string, string?> settings)
    {
        var s = new Scratch();
        s.Write("dispatch/SWITCHES.md", Switches);
        s.Write("dispatch/HEARTBEAT", "2026-11-20T15:10:00Z | dispatch.py\n");
        s.Write("dispatch/HEALED.md", "- 2026-11-20 06:10 CST | server6am apply | x | 4 heal item (code) | ready\n");
        s.Write("dispatch/NO-EMAIL-DAYS.txt", "2026-11-26\n");
        return (s, name => settings.GetValueOrDefault(name));
    }

    [Fact]
    public async Task A_shadow_tick_writes_WATCH_md_whole_and_one_change_line_a_test_and_a_dry_pounce_fails_W2()
    {
        var (s, setting) = World(new() { ["MERGER"] = "on", ["GMAIL_REFRESH_TOKEN"] = "secret-value-never-kept" });
        using (s)
        {
            File.SetLastWriteTimeUtc(Path.Combine(s.Root, "dispatch", "HEARTBEAT"), NineThirteen.UtcDateTime.AddMinutes(-3));
            var result = await new WatchPass(s.Store, setting, new At(NineThirteen)).RunAsync(WatchMode.Shadow, default);

            Assert.True(result.Written);
            string watch = s.Text(WatchTick.File);
            Assert.Contains("| 09:13 | W2 | pounce mode after go-live | FAIL | not set (dry) |", watch, StringComparison.Ordinal);
            Assert.Contains("| W13 | POUNCE_RUN | FAIL | live not set, file live |", watch, StringComparison.Ordinal);
            Assert.Contains("| W16 | no-email day | pass | not a listed day |", watch, StringComparison.Ordinal);
            Assert.Contains("| W17 | the dispatcher's heartbeat | pass | 3 min |", watch, StringComparison.Ordinal);
            Assert.Contains("| W17 | the box card's snapshot | not read |", watch, StringComparison.Ordinal);
            Assert.Contains("| W18 | heal budget and scope | pass | 1 code heals today |", watch, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-value-never-kept", watch, StringComparison.Ordinal);
            string log = s.Text(WatchTick.ChangeLog(new DateOnly(2026, 11, 20)));
            Assert.Equal(result.Results.Count, log.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);

            File.SetLastWriteTimeUtc(Path.Combine(s.Root, "dispatch", "WATCH.md"), NineThirteen.UtcDateTime);
            await new WatchPass(s.Store, setting, new At(NineThirteen.AddMinutes(15))).RunAsync(WatchMode.Shadow, default);
            string again = s.Text(WatchTick.ChangeLog(new DateOnly(2026, 11, 20)));
            Assert.Contains("\tW17\tthe dispatcher's heartbeat\tFAIL\t", again, StringComparison.Ordinal);
            Assert.Contains("\tW17\tthe watch's last tick\tpass\t", again, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_live_pounce_and_matching_switches_pass_and_off_reads_and_writes_nothing()
    {
        var (s, setting) = World(new() { ["MERGER"] = "on", ["POUNCE_RUN"] = "live", ["GMAIL_REFRESH_TOKEN"] = "x" });
        using (s)
        {
            var result = await new WatchPass(s.Store, setting, new At(NineThirteen)).RunAsync(WatchMode.On, default);
            Assert.True(result.Results.Single(r => r.Test == "W2").Pass);
            Assert.True(result.Results.Single(r => r.Test == "W13").Pass);

            using var off = new Scratch();
            var none = await new WatchPass(off.Store, setting, new At(NineThirteen)).RunAsync(WatchMode.Off, default);
            Assert.False(none.Written);
            Assert.False(off.Has(WatchTick.File));
        }
    }

    [Fact]
    public void The_switch_row_s_value_cell_is_read_by_name()
    {
        Assert.Equal("live since 2026-11-19 14:48 CST", WatchPass.Recorded(Switches, "POUNCE_RUN"));
        Assert.Null(WatchPass.Recorded(Switches, "WATCH"));
    }
}
