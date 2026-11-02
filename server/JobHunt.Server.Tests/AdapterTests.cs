using System.Net;
using JobHunt.Server.Infrastructure.Shared.Cli;
using JobHunt.Server.Infrastructure.Shared.Share;
using JobHunt.Server.Infrastructure.Shared.Site;

namespace JobHunt.Server.Tests;

/// <summary>The argument list that is the run's safety, and each adapter on a temp folder with a stand-in process.</summary>
public sealed class AdapterTests : IDisposable
{
    private const string Share = "/mnt/j";
    private const string RunDir = "/mnt/j/nightly/2026-11-03/0600-invites-sonnet-5-5";

    private readonly string folder = Directory.CreateTempSubdirectory("server6am-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private static IReadOnlyList<string> Live(Lane lane = Lane.Invites) => ClaudeArguments.Allowed(lane, Share, RunDir, dry: false);

    [Fact]
    public void A_lane_s_arguments_carry_every_flag_from_its_config()
    {
        var arguments = ClaudeArguments.For(new LaneConfig { Model = "claude-sonnet-5-5", Effort = "low", MaxTurns = 200 }, Lane.Invites, Share, RunDir, dry: false);
        string line = string.Join(' ', arguments);
        Assert.Contains("--model claude-sonnet-5-5 --effort low --max-turns 200 --output-format stream-json --verbose --json-schema", line, StringComparison.Ordinal);
        Assert.Contains("--chrome --strict-mcp-config --add-dir /mnt/j --allowedTools", line, StringComparison.Ordinal);
        Assert.Contains("Read /mnt/j/nightly/LINKEDIN-INVITES-LANE.md and follow it.", line, StringComparison.Ordinal);
        Assert.Contains("DRY_RUN=false", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_is_scoped_to_the_run_folder_and_Bash_is_not_listed()
    {
        Assert.Contains($"Write(//mnt/j/nightly/2026-11-03/0600-invites-sonnet-5-5/**)", Live());
        Assert.Contains($"Edit(//mnt/j/nightly/2026-11-03/0600-invites-sonnet-5-5/**)", Live());
        Assert.DoesNotContain(Live(), tool => tool.StartsWith("Bash", StringComparison.Ordinal));
        Assert.DoesNotContain(Live(), tool => tool is "Write" or "Edit");
    }

    [Fact]
    public void A_dry_run_s_tool_list_holds_no_way_to_click()
    {
        var dry = ClaudeArguments.Allowed(Lane.Apply, Share, RunDir, dry: true);
        Assert.DoesNotContain(dry, tool => tool.EndsWith("__computer", StringComparison.Ordinal));
        Assert.DoesNotContain(dry, tool => tool.EndsWith("__form_input", StringComparison.Ordinal));
        Assert.DoesNotContain(dry, tool => tool.EndsWith("__file_upload", StringComparison.Ordinal));
    }

    [Fact]
    public void No_list_holds_a_batch_or_a_script_and_only_apply_uploads()
    {
        Assert.DoesNotContain(Live(Lane.Apply), tool => tool.EndsWith("browser_batch", StringComparison.Ordinal));
        Assert.DoesNotContain(Live(Lane.Apply), tool => tool.EndsWith("javascript_tool", StringComparison.Ordinal));
        Assert.Contains(Live(Lane.Apply), tool => tool.EndsWith("file_upload", StringComparison.Ordinal));
        Assert.DoesNotContain(Live(Lane.Invites), tool => tool.EndsWith("file_upload", StringComparison.Ordinal));
    }

    [Fact]
    public void The_arguments_never_name_a_way_past_the_permission_check_or_a_shell()
    {
        foreach (Lane lane in RunSlot.Order)
        {
            foreach (bool dry in new[] { true, false })
            {
                var arguments = ClaudeArguments.For(new LaneConfig(), lane, Share, RunDir, dry);
                Assert.DoesNotContain(arguments, argument => argument.Contains("dangerously", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(arguments, argument => argument.Contains("bypass", StringComparison.OrdinalIgnoreCase));
                Assert.True(arguments.ToList().IndexOf("Bash") > arguments.ToList().IndexOf("--disallowedTools"));
                Assert.Contains("--strict-mcp-config", arguments);
            }
        }
    }

    [Fact]
    public void The_prompt_points_at_the_lane_s_doc_and_says_dry_when_it_is()
    {
        Assert.Contains("APPLY-LANE.md", ClaudeArguments.Prompt(Lane.Apply, Share, RunDir, dry: true), StringComparison.Ordinal);
        Assert.Contains("DRY_RUN=true", ClaudeArguments.Prompt(Lane.Apply, Share, RunDir, dry: true), StringComparison.Ordinal);
        Assert.Contains("your tool list holds no click", ClaudeArguments.Prompt(Lane.Apply, Share, RunDir, dry: true), StringComparison.Ordinal);
        Assert.DoesNotContain("no click", ClaudeArguments.Prompt(Lane.Apply, Share, RunDir, dry: false), StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_lane_falls_back_to_the_default_settings()
    {
        Assert.Equal("claude-sonnet-5-5", new ServerConfig().For(Lane.Apply).Model);
    }

    [Fact]
    public async Task The_share_refuses_a_write_the_policy_refuses()
    {
        var share = new ShareFolder(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "Job_Hunt_Tracker.csv"), "kept");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => share.WriteAsync("Job_Hunt_Tracker.csv", "gone", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => share.AppendAsync("state.md", "gone", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => share.WriteNewAsync("failure-log.md", "gone", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => share.CopyInAsync("/tmp/x", "responders.md", default));
        Assert.Equal("kept", await File.ReadAllTextAsync(Path.Combine(folder, "Job_Hunt_Tracker.csv")));
    }

    [Fact]
    public async Task A_spool_file_is_new_and_reads_back()
    {
        var share = new ShareFolder(folder);
        const string Path_ = "inbox/tracker-inbox/20261103-120000Z-server6am-x.csv";
        await share.WriteNewAsync(Path_, "a,b\r\n", default);
        Assert.Equal("a,b\r\n", await share.ReadAsync(Path_, default));
        await Assert.ThrowsAsync<IOException>(() => share.WriteNewAsync(Path_, "c", default));
    }

    [Fact]
    public async Task Reads_lose_the_mark_lists_and_tails_work_and_a_missing_folder_is_empty()
    {
        var share = new ShareFolder(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "a.md"), [0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i']);
        Directory.CreateDirectory(Path.Combine(folder, "sub", "0400-reply-x"));
        Assert.Equal("hi", await share.ReadAsync("a.md", default));
        Assert.Equal("i", await share.TailAsync("a.md", 1, default));
        Assert.Equal(["a.md"], (await share.ListAsync(string.Empty, "*.md", default)).Select(file => file.Path));
        Assert.Empty(await share.ListAsync("nothing", "*", default));
        Assert.Equal(["sub/0400-reply-x"], await share.FoldersAsync("sub", "0400-reply-*", default));
        Assert.Empty(await share.FoldersAsync("nothing", "*", default));
        Assert.True(await share.ExistsAsync("a.md", default));
    }

    [Fact]
    public async Task The_share_resolves_a_link_in_its_root()
    {
        string link = Path.Combine(folder, "link");
        string real = Directory.CreateDirectory(Path.Combine(folder, "real")).FullName;
        File.CreateSymbolicLink(link, real);
        Assert.Equal(real, new ShareFolder(link).Root);
    }

    [Fact]
    public async Task The_lock_turns_a_second_run_away_and_frees_on_dispose()
    {
        var share = new ShareFolder(folder);
        var time = new FixedTime(Morning.At);
        var runLock = new FileRunLock(share, time);
        IAsyncDisposable? first = await runLock.TakeAsync("nightly/2026-11-03", default);
        Assert.NotNull(first);
        Assert.Null(await runLock.TakeAsync("nightly/2026-11-03", default));
        await first.DisposeAsync();
        await using IAsyncDisposable? again = await runLock.TakeAsync("nightly/2026-11-03", default);
        Assert.NotNull(again);
    }

    [Fact]
    public async Task A_lock_past_the_ceiling_is_replaced()
    {
        var share = new ShareFolder(folder);
        var time = new FixedTime(Morning.At);
        var runLock = new FileRunLock(share, time);
        await share.WriteAsync("nightly/2026-11-03/0600.lock", $"999999\t{Morning.At.AddHours(-5).ToUnixTimeSeconds()}\n", default);
        await using IAsyncDisposable? taken = await runLock.TakeAsync("nightly/2026-11-03", default);
        Assert.NotNull(taken);
    }

    [Fact]
    public async Task A_lock_with_a_broken_body_turns_the_run_away()
    {
        var share = new ShareFolder(folder);
        await share.WriteAsync("nightly/2026-11-03/0600.lock", "junk", default);
        Assert.Null(await new FileRunLock(share, new FixedTime(Morning.At)).TakeAsync("nightly/2026-11-03", default));
    }

    [Fact]
    public async Task A_stale_lock_whose_id_is_not_this_program_is_replaced_and_nothing_is_killed()
    {
        var share = new ShareFolder(folder);
        using var bystander = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/sleep", ["20"]) { UseShellExecute = false })!;
        await share.WriteAsync("nightly/2026-11-03/0600.lock", $"{bystander.Id}\t{Morning.At.AddHours(-5).ToUnixTimeSeconds()}\n", default);
        await using IAsyncDisposable? taken = await new FileRunLock(share, new FixedTime(Morning.At)).TakeAsync("nightly/2026-11-03", default);
        Assert.NotNull(taken);
        Assert.False(bystander.HasExited);
        bystander.Kill();
    }

    [Fact]
    public async Task The_mount_is_fresh_when_the_rules_read()
    {
        var share = new MemoryShare();
        share.Files["systemRules.md"] = "rules";
        Assert.Null(await new JDriveMount(share).FreshAsync(default));
    }

    [Fact]
    public async Task The_cli_keeps_the_stream_and_finds_the_result()
    {
        string stream = Path.Combine(folder, "stream.jsonl");
        var request = new ClaudeRequest(["-c", """echo '{"type":"system"}'; echo '{"type":"result","x":1}'; exit 3"""], folder, stream, TimeSpan.FromSeconds(20));
        ClaudeRun run = await new ClaudeCli("/bin/sh").RunAsync(request, default);
        Assert.Equal(3, run.ExitCode);
        Assert.False(run.TimedOut);
        Assert.Equal("""{"type":"result","x":1}""", run.ResultLine);
        Assert.Equal(2, (await File.ReadAllLinesAsync(stream)).Length);
    }

    [Fact]
    public async Task The_cli_kills_a_run_at_its_limit()
    {
        var request = new ClaudeRequest(["-c", "sleep 30"], folder, Path.Combine(folder, "stream.jsonl"), TimeSpan.FromMilliseconds(300));
        ClaudeRun run = await new ClaudeCli("/bin/sh").RunAsync(request, default);
        Assert.True(run.TimedOut);
        Assert.Null(run.ResultLine);
    }

    [Fact]
    public async Task A_stop_kills_the_run_and_hands_back_what_was_seen()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var request = new ClaudeRequest(["-c", "echo line; sleep 30"], folder, Path.Combine(folder, "stream.jsonl"), TimeSpan.FromSeconds(30));
        ClaudeRun run = await new ClaudeCli("/bin/sh").RunAsync(request, stop.Token);
        Assert.True(run.TimedOut);
    }

    [Fact]
    public async Task A_share_that_refuses_the_stream_does_not_cost_the_run()
    {
        var request = new ClaudeRequest(["-c", "echo '{\"type\":\"result\"}'"], folder, Path.Combine(folder, "nothing-here", "stream.jsonl"), TimeSpan.FromSeconds(20));
        ClaudeRun run = await new ClaudeCli("/bin/sh").RunAsync(request, default);
        Assert.NotNull(run.ResultLine);
    }

    [Fact]
    public async Task The_helpers_read_the_code_and_the_exit()
    {
        await File.WriteAllTextAsync(Path.Combine(folder, "make_ref.py"), "echo 'code abc123 for Pat (Acme, LinkedIn) spooled to x'\n");
        await File.WriteAllTextAsync(Path.Combine(folder, "check_aiisms.py"), "exit 1\n");
        var helpers = new ShareHelpers("/bin/sh", new ShareFolder(folder));
        Assert.Equal("abc123", await helpers.MintAsync("Pat", "Acme", default));
        Assert.False(await helpers.CleanAsync("text", default));
    }

    [Fact]
    public async Task A_helper_that_fails_mints_nothing()
    {
        await File.WriteAllTextAsync(Path.Combine(folder, "make_ref.py"), "exit 5\n");
        Assert.Null(await new ShareHelpers("/bin/sh", new ShareFolder(folder)).MintAsync("Pat", "Acme", default));
    }

    [Fact]
    public void The_desktop_is_the_port_the_run_asks()
    {
        Assert.IsAssignableFrom<IDesktop>(new ChromeDesktop());
    }

    [Fact]
    public async Task With_no_key_the_usage_is_a_line_and_no_post()
    {
        using var http = new HttpClient(new Answering(HttpStatusCode.OK));
        var usage = new SiteUsage(new ServerConfig { SecretsFile = Path.Combine(folder, "none.json") }, http);
        Assert.Equal("usage not reported: no key", await usage.ReportAsync("{}", default));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "usage reported")]
    [InlineData(HttpStatusCode.Forbidden, "usage not reported: the site answered 403")]
    public async Task With_a_key_the_usage_posts_and_a_refusal_is_a_line(HttpStatusCode status, string line)
    {
        string secrets = Path.Combine(folder, "secrets.json");
        await File.WriteAllTextAsync(secrets, """{"adminKey":"k"}""");
        var handler = new Answering(status);
        using var http = new HttpClient(handler);
        var usage = new SiteUsage(new ServerConfig { SecretsFile = secrets, Site = "https://site.test" }, http);
        Assert.Equal(line, await usage.ReportAsync("{}", default));
        Assert.Equal("https://site.test/api/admin/usage", handler.Asked?.RequestUri?.ToString());
        Assert.Equal("k", handler.Asked?.Headers.GetValues("X-Admin-Key").Single());
    }

    [Fact]
    public async Task A_site_that_hangs_is_a_line_and_never_a_failed_run()
    {
        string secrets = Path.Combine(folder, "secrets.json");
        await File.WriteAllTextAsync(secrets, """{"adminKey":"k"}""");
        using var http = new HttpClient(new Hanging());
        var usage = new SiteUsage(new ServerConfig { SecretsFile = secrets, Site = "https://site.test" }, http);
        Assert.StartsWith("usage not reported", await usage.ReportAsync("{}", default), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_broken_secrets_file_is_no_key()
    {
        string secrets = Path.Combine(folder, "secrets.json");
        await File.WriteAllTextAsync(secrets, "{not json");
        using var http = new HttpClient(new Answering(HttpStatusCode.OK));
        Assert.Equal("usage not reported: no key", await new SiteUsage(new ServerConfig { SecretsFile = secrets }, http).ReportAsync("{}", default));
    }

    [Fact]
    public void The_recount_can_scroll_a_live_page_and_never_fill_a_form()
    {
        var live = ClaudeArguments.Recount(new LaneConfig(), Share, RunDir, dry: false);
        var dry = ClaudeArguments.Recount(new LaneConfig(), Share, RunDir, dry: true);
        Assert.Contains(live, tool => tool.EndsWith("__computer", StringComparison.Ordinal));
        Assert.DoesNotContain(live, tool => tool.EndsWith("__form_input", StringComparison.Ordinal));
        Assert.DoesNotContain(dry, tool => tool.EndsWith("__computer", StringComparison.Ordinal));
        Assert.Contains("Bash", live);
        Assert.True(live.ToList().IndexOf("Bash") > live.ToList().IndexOf("--disallowedTools"));
    }

    [Fact]
    public async Task The_probe_passes_on_a_clean_result_and_fails_on_a_denial()
    {
        var good = new ScriptedClaude(_ => new ClaudeRun(0, false, """{"type":"result","is_error":false,"permission_denials":[],"result":"all worked"}"""));
        (int exit, string output) = await new BrowserProbe(good, new FakeDesktop(), folder).ExecuteAsync(default);
        Assert.Equal(0, exit);
        Assert.Contains("all worked", output, StringComparison.Ordinal);
        Assert.DoesNotContain("linkedin", string.Join(' ', BrowserProbe.Arguments()), StringComparison.OrdinalIgnoreCase);

        var denied = new ScriptedClaude(_ => new ClaudeRun(0, false, """{"type":"result","is_error":false,"permission_denials":[{"tool_name":"x"}],"result":"no"}"""));
        Assert.Equal(1, (await new BrowserProbe(denied, new FakeDesktop(), folder).ExecuteAsync(default)).Exit);
    }

    [Fact]
    public async Task The_probe_reports_a_dead_browser_and_a_dead_cli()
    {
        var none = new ScriptedClaude(_ => new ClaudeRun(1, false, null));
        Assert.Equal(1, (await new BrowserProbe(none, new FakeDesktop("no Chrome"), folder).ExecuteAsync(default)).Exit);
        Assert.Contains("no result", (await new BrowserProbe(none, new FakeDesktop(), folder).ExecuteAsync(default)).Output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_blockers_list_opens_with_the_rules_line_and_names_each_posting()
    {
        string text = Blockers.Render(new DateOnly(2026, 11, 3), [new Posting("Acme", "Staff", "https://x", "Workday, ACCOUNT WALL", "s:1")]);
        Assert.StartsWith("> RULES FIRST", text, StringComparison.Ordinal);
        Assert.Contains("## 1. Acme, Staff", text, StringComparison.Ordinal);
        Assert.Contains("Where it stuck: Workday, ACCOUNT WALL.", text, StringComparison.Ordinal);
    }

    private sealed class Hanging : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TaskCanceledException("the client's timeout");
    }

    private sealed class Answering(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked = request;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    [Fact]
    public async Task A_young_lock_from_a_process_that_is_gone_is_replaced_so_the_next_run_can_say_what_happened()
    {
        var share = new ShareFolder(folder);
        await share.WriteAsync("nightly/2026-11-03/0600.lock", $"999999\t{Morning.At.AddMinutes(-5).ToUnixTimeSeconds()}\n", default);
        await using IAsyncDisposable? taken = await new FileRunLock(share, new FixedTime(Morning.At)).TakeAsync("nightly/2026-11-03", default);
        Assert.NotNull(taken);
    }

    [Fact]
    public async Task A_lock_the_share_will_not_let_go_of_does_not_crash_the_close()
    {
        var share = new ShareFolder(folder);
        IAsyncDisposable? taken = await new FileRunLock(share, new FixedTime(Morning.At)).TakeAsync("nightly/2026-11-03", default);
        string held = Path.Combine(folder, "nightly/2026-11-03/0600.lock");
        File.Delete(held);
        Directory.CreateDirectory(held);
        await taken!.DisposeAsync();
    }

    [Theory]
    [InlineData(new[] { "run" }, false, null)]
    [InlineData(new[] { "run", "--dry-run" }, true, null)]
    [InlineData(new[] { "run", "--lane", "apply", "--dry-run" }, true, Lane.Apply)]
    public void The_command_line_parses(string[] arguments, bool dry, Lane? lane)
    {
        Assert.Equal(new RunOptions(dry, lane), RunOptions.Parse(arguments));
    }

    [Theory]
    [InlineData("run", "--dryrun")]
    [InlineData("run", "--dry_run")]
    [InlineData("run", "dry")]
    [InlineData("run", "--lane")]
    [InlineData("run", "--lane", "everything")]
    public void A_mistyped_flag_never_falls_through_to_a_live_run(params string[] arguments)
    {
        Assert.Null(RunOptions.Parse(arguments));
    }

    [Fact]
    public void Nothing_but_the_browser_and_the_run_folder_is_left_open_to_a_lane()
    {
        var arguments = ClaudeArguments.For(new LaneConfig(), Lane.Apply, Share, RunDir, dry: false);
        foreach (string tool in new[] { "Workflow", "Skill", "SendMessage", "PushNotification", "ScheduleWakeup", "CronDelete", "NotebookEdit", "mcp__claude-in-chrome__shortcuts_execute", "mcp__claude-in-chrome__gif_creator" })
        {
            Assert.Contains(tool, arguments);
        }
        Assert.Contains($"Read(//{Share.TrimStart('/')}/standard-answers.md)", arguments);
        Assert.Contains($"Read(//{Share.TrimStart('/')}/targeting-criteria.md)", arguments);
    }

    [Fact]
    public async Task A_tailored_resume_comes_back_with_its_path_headline_and_hash()
    {
        string script = Path.Combine(folder, "build_tailored_resume.py");
        await File.WriteAllTextAsync(script, "echo \"headline      : STAFF ENGINEER\"\necho \"send copy     : /out/StevenStoutResume_2026-10-29.pdf\"\necho \"sha256        : abc\"\n");
        ResumeBuild built = await new ShareHelpers("/bin/sh", new ShareFolder(folder)).BuildAsync("Staff Engineer", "Acme", "/out", default);
        Assert.Equal(new ResumeBuild("/out/StevenStoutResume_2026-10-29.pdf", "STAFF ENGINEER", "abc"), built);
    }

    [Fact]
    public async Task A_resume_that_will_not_build_says_why()
    {
        string script = Path.Combine(folder, "build_tailored_resume.py");
        await File.WriteAllTextAsync(script, "echo \"STOP. The headline does not sit whole on one line.\"\nexit 1\n");
        ResumeBuild built = await new ShareHelpers("/bin/sh", new ShareFolder(folder)).BuildAsync("Staff Engineer", "Acme", "/out", default);
        Assert.Contains("one line", built.Failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Every_run_can_pick_the_box_s_browser_and_none_can_page_a_person(bool dry)
    {
        foreach (var arguments in new[] { ClaudeArguments.For(new LaneConfig(), Lane.Invites, Share, RunDir, dry), ClaudeArguments.Recount(new LaneConfig(), Share, RunDir, dry), BrowserProbe.Arguments() })
        {
            Assert.Contains("mcp__claude-in-chrome__list_connected_browsers", arguments);
            Assert.Contains("mcp__claude-in-chrome__select_browser", arguments);
        }
        int refused = ClaudeArguments.For(new LaneConfig(), Lane.Invites, Share, RunDir, dry).ToList().IndexOf("--disallowedTools");
        var after = ClaudeArguments.For(new LaneConfig(), Lane.Invites, Share, RunDir, dry).Skip(refused);
        Assert.Contains("mcp__claude-in-chrome__switch_browser", after);
        Assert.DoesNotContain("mcp__claude-in-chrome__select_browser", after);
    }

    [Fact]
    public void The_probe_says_which_browser_to_pick()
    {
        Assert.Contains("Linux", BrowserProbe.Arguments()[1], StringComparison.Ordinal);
    }
}
