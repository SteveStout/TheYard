using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The email leg of a caught role, in shadow: the leader the slot run's feed named is looked up once with the finder, an
/// answer is kept only when it carries the leader's own name on the company's own domain, the month's lookups are the
/// campaign's count so the free plan's 50 hold across both, a company that had a real email in the last 30 days gets no
/// lookup, and nothing is ever sent: a kept address is written as a shadow line until Steve answers the email ruling.
/// </summary>
public sealed class PounceEmailTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 16, 20, 0, TimeSpan.Zero);

    private const string Handoff = """{"key":12786,"company":"Zephyr Labs","title":"Staff Software Engineer - Web","url":"https://job-boards.greenhouse.io/zephyr/jobs/1","band":"PASS","live_at":"2026-11-11T15:25:00+00:00","live_known":true,"budget":false,"passed_at":"2026-11-11T16:05:00+00:00","company_key":"zephyrlabs.com"}""";

    private const string Queue = "# QUEUE-pounce-2026-11-11-12786\n\n### 1. Dana Whitfield, Zephyr Labs (VP Engineering). Code [code].\n- Seat: VP Engineering\n- Profile: https://www.linkedin.com/in/dana-whitfield\n\n```\nDana,\n```\n";

    [Fact]
    public void The_leader_is_the_first_block_s_name_and_the_hand_off_carries_the_company_s_domain()
    {
        Assert.Equal("Dana Whitfield", PounceEmail.Leader(Queue));
        Assert.Equal("Bo Example", PounceEmail.Leader("### 1. Bo Example, Aledade (VP). MESSAGE\n"));
        Assert.Null(PounceEmail.Leader("### 1. Zephyr Labs (Director). Code [code].\n"));
        Assert.Null(PounceEmail.Leader("# QUEUE-pounce-2026-11-11-12786\n\nNo candidate.\n"));

        var role = new CaughtRole(12786, "Zephyr Labs", "Staff Software Engineer - Web", "https://job-boards.greenhouse.io/zephyr/jobs/1", "PASS",
            new DateTimeOffset(2026, 11, 11, 15, 25, 0, TimeSpan.Zero), true, false, new DateTimeOffset(2026, 11, 11, 16, 5, 0, TimeSpan.Zero), "zephyrlabs.com");
        Assert.Equal(Handoff, PounceHandoff.Json(role));
        Assert.DoesNotContain("company_key", PounceHandoff.Json(role with { CompanyKey = null }), StringComparison.Ordinal);
        Assert.Equal(new EmailRole(12786, "Zephyr Labs", "zephyrlabs.com", role.PassedAt), PounceEmail.Read(Handoff));
        Assert.Null(PounceEmail.Read("{\"company\":\"x\"}"));
        Assert.Null(PounceEmail.Read("not json"));
    }

    [Theory]
    [InlineData("dwhitfield@zephyrlabs.com", "https://zephyrlabs.com/team", null)]
    [InlineData("dana@zephyrlabs.com", "https://zephyrlabs.com/team", null)]
    [InlineData("info@zephyrlabs.com", "https://zephyrlabs.com/team", "not the person's own name")]
    [InlineData("careers@zephyrlabs.com", "https://zephyrlabs.com/team", "not the person's own name")]
    [InlineData("dana.whitfield@othercorp.com", "https://othercorp.com/team", "not on the company's domain")]
    [InlineData("dana.whitfield@gmail.com", "https://gmail.com", "not on the company's domain")]
    [InlineData("dwhitfield@zephyrlabs.com", "http://zephyrlabs.com/team", "no https page it was read from")]
    [InlineData("dwhitfield", "https://zephyrlabs.com/team", "not an address")]
    public void A_finder_s_address_is_kept_only_when_it_carries_the_leader_s_own_name_on_the_company_s_domain(string address, string source, string? reason) =>
        Assert.Equal(reason, PounceEmail.Refuse(address, source, "Dana Whitfield", "zephyrlabs.com"));

    [Fact]
    public void The_finder_s_month_is_the_campaign_s_own_count_file_so_the_monthly_cap_holds_across_both()
    {
        var local = CentralClock.Local(Now);
        Assert.Equal(AddressSearch.CountPath("hunter", local), PounceEmail.CountPath("hunter", local));
        Assert.Equal("campaign/finder/hunter-2026-11.txt", PounceEmail.CountPath("hunter", local));
    }

    [Fact]
    public async Task A_caught_role_s_leader_is_looked_up_once_and_kept_in_shadow_never_sent()
    {
        using var scratch = new Scratch();
        scratch.Write("nightly/pounce/20261111-160500Z-12786.json", Handoff);
        scratch.Write("linkedin-proof/QUEUE-pounce-2026-11-11-12786.md", Queue);
        scratch.Write("campaign/finder/hunter-2026-11.txt", "7");
        var finder = new Finder("dwhitfield@zephyrlabs.com", "https://zephyrlabs.com/team");
        var emails = new PounceEmails(scratch.Store, finder, new Sends(), new Fixed(Now), 50);

        var pass = await emails.RunAsync(PounceHandoff.Live, CancellationToken.None);

        Assert.Equal(new EmailPass(1, 1, 1, 1, 0, 0, 0), pass);
        Assert.Equal(["Dana Whitfield|zephyrlabs.com"], finder.Asked);
        Assert.Equal("8", scratch.Text("campaign/finder/hunter-2026-11.txt"));
        string marker = scratch.Text("nightly/pounce/emails/12786.md");
        Assert.Contains("SHADOW", marker, StringComparison.Ordinal);
        Assert.Contains("dwhitfield@zephyrlabs.com", marker, StringComparison.Ordinal);
        Assert.Contains("Nothing was sent", marker, StringComparison.Ordinal);
        Assert.Contains("12786 Zephyr Labs | Dana Whitfield | shadow, kept dwhitfield@zephyrlabs.com", scratch.Text(PounceEmail.Log), StringComparison.Ordinal);
        Assert.DoesNotContain((char)0x2014, marker);

        var again = await emails.RunAsync(PounceHandoff.Dry, CancellationToken.None);
        Assert.Equal(new EmailPass(1, 0, 0, 0, 0, 1, 0), again);
        Assert.Single(finder.Asked);
    }

    [Fact]
    public async Task An_address_without_the_leader_s_name_is_refused_and_written_down_once()
    {
        using var scratch = new Scratch();
        scratch.Write("nightly/pounce/20261111-160500Z-12786.json", Handoff);
        scratch.Write("nightly/2026-11-11/pounce-12786-feed-opus-5-5-dry/QUEUE-pounce-2026-11-11-12786.md", Queue);
        var finder = new Finder("info@zephyrlabs.com", "https://zephyrlabs.com/contact");

        var pass = await new PounceEmails(scratch.Store, finder, new Sends(), new Fixed(Now), 50).RunAsync(PounceHandoff.Dry, CancellationToken.None);

        Assert.Equal(new EmailPass(1, 1, 1, 0, 1, 0, 0), pass);
        Assert.Contains("refused: not the person's own name", scratch.Text("nightly/pounce/emails/12786.md"), StringComparison.Ordinal);
        Assert.Equal("1", scratch.Text("campaign/finder/hunter-2026-11.txt"));
    }

    [Fact]
    public async Task Nothing_is_asked_when_the_switch_is_off_the_month_is_spent_the_company_had_an_email_or_no_leader_is_named()
    {
        using (var off = new Scratch())
        {
            off.Write("nightly/pounce/20261111-160500Z-12786.json", Handoff);
            off.Write("linkedin-proof/QUEUE-pounce-2026-11-11-12786.md", Queue);
            var finder = new Finder("dwhitfield@zephyrlabs.com", "https://zephyrlabs.com/team");
            var pass = await new PounceEmails(off.Store, finder, new Sends(), new Fixed(Now), 50).RunAsync(PounceHandoff.Off, CancellationToken.None);
            Assert.Equal("switch_off", pass.Blocked);
            Assert.Empty(finder.Asked);
            Assert.False(off.Has(PounceEmail.Log));
        }

        using (var spent = new Scratch())
        {
            spent.Write("nightly/pounce/20261111-160500Z-12786.json", Handoff);
            spent.Write("linkedin-proof/QUEUE-pounce-2026-11-11-12786.md", Queue);
            spent.Write("campaign/finder/hunter-2026-11.txt", "50");
            var finder = new Finder("dwhitfield@zephyrlabs.com", "https://zephyrlabs.com/team");
            var pass = await new PounceEmails(spent.Store, finder, new Sends(), new Fixed(Now), 50).RunAsync(PounceHandoff.Dry, CancellationToken.None);
            Assert.Equal((1, 0, 1), (pass.Leaders, pass.Asked, pass.Waiting));
            Assert.Empty(finder.Asked);
            Assert.False(spent.Has("nightly/pounce/emails/12786.md"));
        }

        using (var emailed = new Scratch())
        {
            emailed.Write("nightly/pounce/20261111-160500Z-12786.json", Handoff);
            emailed.Write("linkedin-proof/QUEUE-pounce-2026-11-11-12786.md", Queue);
            var finder = new Finder("dwhitfield@zephyrlabs.com", "https://zephyrlabs.com/team");
            var sends = new Sends { All = { new EmailTouch("Zephyr Labs Inc", "pat@zephyrlabs.com", "Pat Lee", Now.AddDays(-20)) } };
            var pass = await new PounceEmails(emailed.Store, finder, sends, new Fixed(Now), 50).RunAsync(PounceHandoff.Dry, CancellationToken.None);
            Assert.Equal((0, 1), (pass.Asked, pass.Refused));
            Assert.Empty(finder.Asked);
            Assert.Contains("one email a company a month", emailed.Text("nightly/pounce/emails/12786.md"), StringComparison.Ordinal);
        }

        using var waiting = new Scratch();
        waiting.Write("nightly/pounce/20261111-160500Z-12786.json", Handoff);
        waiting.Write("nightly/pounce/20261109-160500Z-12001.json", Handoff.Replace("12786", "12001", StringComparison.Ordinal).Replace("2026-11-11T16:05", "2026-11-09T16:05", StringComparison.Ordinal));
        var none = new Finder("dwhitfield@zephyrlabs.com", "https://zephyrlabs.com/team");
        var quiet = await new PounceEmails(waiting.Store, none, new Sends(), new Fixed(Now), 50).RunAsync(PounceHandoff.Dry, CancellationToken.None);
        Assert.Equal(new EmailPass(1, 0, 0, 0, 0, 0, 1), quiet);
        Assert.Empty(none.Asked);
        Assert.False(waiting.Has("nightly/pounce/emails/12786.md"));
    }

    [Fact]
    public void A_key_that_is_a_number_but_not_a_whole_long_reads_as_no_hand_off()
    {
        Assert.Null(PounceEmail.Read(Handoff.Replace("\"key\":12786", "\"key\":1.5", StringComparison.Ordinal)));
        Assert.Null(PounceEmail.Read(Handoff.Replace("\"key\":12786", "\"key\":99999999999999999999", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_settled_role_s_log_line_is_written_on_a_later_pass_when_its_pass_stopped_short_and_never_twice()
    {
        using var scratch = new Scratch();
        scratch.Write("nightly/pounce/20261111-160500Z-12786.json", Handoff);
        scratch.Write("linkedin-proof/QUEUE-pounce-2026-11-11-12786.md", Queue);
        scratch.Write("nightly/pounce/20261111-160600Z-12787.json", Handoff.Replace("12786", "12787", StringComparison.Ordinal).Replace("Zephyr Labs", "Quill Systems", StringComparison.Ordinal).Replace("zephyrlabs.com", "quill.io", StringComparison.Ordinal));
        scratch.Write("linkedin-proof/QUEUE-pounce-2026-11-11-12787.md", Queue.Replace("12786", "12787", StringComparison.Ordinal).Replace("Dana Whitfield, Zephyr Labs", "Omar Haddad, Quill Systems", StringComparison.Ordinal));
        var finder = new Finder("dwhitfield@zephyrlabs.com", "https://zephyrlabs.com/team") { FailOn = "quill.io" };

        await Assert.ThrowsAsync<HttpRequestException>(() => new PounceEmails(scratch.Store, finder, new Sends(), new Fixed(Now), 50).RunAsync(PounceHandoff.Dry, CancellationToken.None));
        Assert.True(scratch.Has("nightly/pounce/emails/12786.md"));

        finder.FailOn = null;
        var flaky = new FailingAppend(scratch.Store);
        await Assert.ThrowsAsync<IOException>(() => new PounceEmails(flaky, finder, new Sends(), new Fixed(Now.AddMinutes(15)), 50).RunAsync(PounceHandoff.Dry, CancellationToken.None));
        Assert.True(scratch.Has("nightly/pounce/emails/12787.md"));

        await new PounceEmails(scratch.Store, finder, new Sends(), new Fixed(Now.AddMinutes(30)), 50).RunAsync(PounceHandoff.Dry, CancellationToken.None);
        await new PounceEmails(scratch.Store, finder, new Sends(), new Fixed(Now.AddMinutes(45)), 50).RunAsync(PounceHandoff.Dry, CancellationToken.None);
        string[] lines = scratch.Text(PounceEmail.Log).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines, line => line.Contains(" 12786 Zephyr Labs | Dana Whitfield | shadow, kept dwhitfield@zephyrlabs.com", StringComparison.Ordinal));
        Assert.Single(lines, line => line.Contains(" 12787 Quill Systems | Omar Haddad | refused: not on the company's domain", StringComparison.Ordinal));
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public async Task A_settled_role_s_log_line_is_recovered_on_a_pass_after_its_hand_off_has_left_the_day()
    {
        using var scratch = new Scratch();
        scratch.Write("nightly/pounce/20261111-160500Z-12786.json", Handoff);
        EmailRole role = PounceEmail.Read(Handoff)!;
        DateTimeOffset settled = role.PassedAt.AddHours(23).AddMinutes(55);
        string line = PounceEmail.LogLine(CentralClock.Local(settled), role, "Dana Whitfield", "refused: one email a company a month");
        scratch.Write("nightly/pounce/emails/12786.md", PounceEmail.Settled(role, "Dana Whitfield", "one email a company a month", settled) + PounceEmail.LogField + line + "\n");
        var emails = new PounceEmails(scratch.Store, null, new Sends(), new Fixed(role.PassedAt.AddHours(24).AddMinutes(10)), 50);

        Assert.Equal(new EmailPass(0, 0, 0, 0, 0, 0, 0), await emails.RunAsync(PounceHandoff.Dry, CancellationToken.None));
        await emails.RunAsync(PounceHandoff.Dry, CancellationToken.None);

        Assert.Equal([line], scratch.Text(PounceEmail.Log).Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Finder(string address, string source) : ILeaderFinder
    {
        public List<string> Asked { get; } = [];

        public string? FailOn { get; set; }

        public string Name => "hunter";

        public Task<LeaderAddress?> FindAsync(string fullName, string domain, CancellationToken ct)
        {
            if (domain == FailOn)
            {
                throw new HttpRequestException("the finder did not answer");
            }
            Asked.Add($"{fullName}|{domain}");
            return Task.FromResult<LeaderAddress?>(new LeaderAddress(address, source));
        }
    }

    private sealed class Sends : IPounceReader
    {
        public List<EmailTouch> All { get; } = [];

        public Task<bool> ReadyAsync(CancellationToken ct) => Task.FromResult(true);

        public Task<IReadOnlyList<OpenRole>> OpenAsync(DateTimeOffset liveSince, CancellationToken ct) => Task.FromResult<IReadOnlyList<OpenRole>>([]);

        public Task<IReadOnlyList<(string Company, string Title)>> PassedSinceAsync(DateTimeOffset judgedSince, CancellationToken ct) => Task.FromResult<IReadOnlyList<(string, string)>>([]);

        public Task<IReadOnlyList<(DateTimeOffset Live, DateTimeOffset? Touch)>> CohortAsync(DateTimeOffset liveSince, CancellationToken ct) => Task.FromResult<IReadOnlyList<(DateTimeOffset, DateTimeOffset?)>>([]);

        public Task<IReadOnlyList<EmailTouch>> EmailsSinceAsync(DateTimeOffset since, CancellationToken ct) => Task.FromResult<IReadOnlyList<EmailTouch>>([.. All.Where(e => e.SentAt >= since)]);
    }

    /// <summary>The share, whose first append fails, as a share that stops answering half way through a pass.</summary>
    private sealed class FailingAppend(IMergeStore inner) : IMergeStore
    {
        private bool _failed;

        public Task<bool> ExistsAsync(string path, CancellationToken ct) => inner.ExistsAsync(path, ct);

        public Task<byte[]> ReadAsync(string path, CancellationToken ct) => inner.ReadAsync(path, ct);

        public Task<MergeEntry?> StatAsync(string path, CancellationToken ct) => inner.StatAsync(path, ct);

        public Task<IReadOnlyList<MergeEntry>> ListFilesAsync(string folder, CancellationToken ct) => inner.ListFilesAsync(folder, ct);

        public Task<IReadOnlyList<string>> ListFoldersAsync(string folder, CancellationToken ct) => inner.ListFoldersAsync(folder, ct);

        public Task EnsureFolderAsync(string folder, CancellationToken ct) => inner.EnsureFolderAsync(folder, ct);

        public Task WriteAsync(string path, byte[] bytes, CancellationToken ct) => inner.WriteAsync(path, bytes, ct);

        public Task ReplaceAsync(string tempPath, string path, CancellationToken ct) => inner.ReplaceAsync(tempPath, path, ct);

        public Task MoveAsync(string path, string destination, CancellationToken ct) => inner.MoveAsync(path, destination, ct);

        public Task AppendAsync(string path, byte[] bytes, CancellationToken ct)
        {
            if (!_failed)
            {
                _failed = true;
                throw new IOException("the share did not answer");
            }
            return inner.AppendAsync(path, bytes, ct);
        }
    }
}
