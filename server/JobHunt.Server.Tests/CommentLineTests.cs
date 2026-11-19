using System.Text.Json;

namespace JobHunt.Server.Tests;

/// <summary>
/// The comment line: right after the feed it reads Steve's own comments of the day, the program drops every one a skip rule
/// names, rolls about one in three of the rest, mints a code each and writes the line, and an edit counts only when the
/// reloaded comment holds his words, the line last and the code once.
/// </summary>
public sealed class CommentLineTests
{
    private const string Folder = "nightly/2026-11-03/0600-commentline-opus-5-5";
    private const string Tie = "puts a modular monolith with enforced boundaries into a live system";

    private static OwnComment Comment(string text = "We went back to a modular monolith and deploys got boring again, which is the point.", string age = "3h", string fit = "architecture", string topic = "modular monoliths", string url = "https://www.linkedin.com/feed/update/urn:li:activity:9?commentUrn=urn%3Ali%3Acomment%3A9") =>
        new(url, "Marcus Hale", "Fabrikam", topic, text, age, false, false, fit, Tie);

    private static string Comments(string fixture) => Fixture.Read(fixture);

    [Fact]
    public void The_comment_line_runs_right_after_the_feed_and_is_held_like_a_drive()
    {
        Assert.Equal([Lane.Feed, Lane.Commentline, Lane.Invites, Lane.Messages, Lane.Network, Lane.Apply], RunSlot.Order);
        Assert.Equal("commentline", RunSlot.Word(Lane.Commentline));
        Assert.Equal("COMMENTLINE-LANE.md", RunSlot.Doc(Lane.Commentline));
        Assert.Equal(Lane.Commentline, RunSlot.Parse("commentline"));
        Assert.DoesNotContain(Lane.Commentline, RunSlot.RoleOrder);
        Assert.Equal((24, 168), (CommentPicks.Window(false), CommentPicks.Window(true)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(1119)]
    [InlineData(20261119)]
    public void The_roll_picks_one_in_three_of_thirty(int seed)
    {
        var comments = CommentPicks.Parse(Fixture.Read("comments-30.json"));
        Assert.Equal(30, comments.Count);
        CommentRoll roll = CommentPicks.Roll(comments, 24, new Random(seed));
        Assert.Equal((30, 10, 20), (roll.Eligible, roll.Picked.Count, roll.Dropped.Count));
        Assert.All(roll.Dropped, dropped => Assert.Equal("not picked by the roll (10 of 30)", dropped.Reason));
    }

    [Fact]
    public void Over_many_mornings_each_comment_is_picked_a_third_of_the_time_and_no_morning_is_fixed()
    {
        var comments = CommentPicks.Parse(Fixture.Read("comments-30.json")).Take(10).ToList();
        var random = new Random(42);
        var counts = new int[comments.Count];
        var mornings = new HashSet<string>();
        const int Runs = 3000;
        for (int run = 0; run < Runs; run++)
        {
            CommentRoll roll = CommentPicks.Roll(comments, 24, random);
            Assert.InRange(roll.Picked.Count, 3, 4);
            mornings.Add(string.Join(',', roll.Picked.Select(c => comments.IndexOf(c))));
            foreach (OwnComment picked in roll.Picked)
            {
                counts[comments.IndexOf(picked)]++;
            }
        }
        Assert.All(counts, count => Assert.InRange(count / (double)Runs, 0.28, 0.39));
        Assert.InRange(counts.Sum() / (double)(Runs * comments.Count), 0.32, 0.35);
        Assert.True(mornings.Count > 100);
    }

    [Fact]
    public void A_morning_of_one_or_two_still_rolls_and_the_cap_is_fifteen()
    {
        int ones = Enumerable.Range(0, 3000).Count(seed => CommentPicks.Roll([Comment()], 24, new Random(seed)).Picked.Count == 1);
        Assert.InRange(ones / 3000.0, 0.30, 0.37);
        var many = Enumerable.Range(0, 60).Select(n => Comment(text: $"Comment {n}: the modular monolith kept our deploys boring and our boundaries honest.")).ToList();
        Assert.Equal(15, CommentPicks.Roll(many, 24, new Random(3)).Picked.Count);
        Assert.Empty(CommentPicks.Roll([], 24, new Random(3)).Picked);
    }

    [Fact]
    public void Every_skip_rule_drops_its_fixture_case()
    {
        string json = Fixture.Read("comments-skips.json");
        var cases = JsonDocument.Parse(json).RootElement.GetProperty("comments").EnumerateArray().Select(c => c.GetProperty("case").GetString()!).ToList();
        var comments = CommentPicks.Parse(json);
        CommentRoll roll = CommentPicks.Roll(comments, 24, new Random(1));
        Assert.Equal(1, roll.Eligible);
        var reasons = roll.Dropped.Where(d => !d.Reason.StartsWith("not picked", StringComparison.Ordinal)).Select(d => d.Reason).ToList();
        Assert.Equal(cases.Count - 1, reasons.Count);
        for (int i = 0, r = 0; i < cases.Count; i++)
        {
            if (i == cases.Count - 2)
            {
                continue;
            }
            Assert.Contains(cases[i], reasons[r++], StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("Congrats Jo, well deserved! 🎉", true)]
    [InlineData("🔥👏", true)]
    [InlineData("Commenting for better reach, great thread", true)]
    [InlineData("Agreed.", true)]
    [InlineData("Great post. Split queries saved us more than any index did on SQL Server.", false)]
    [InlineData("Records cleaned up half our DTO layer in C#.", false)]
    public void A_pure_reaction_is_emoji_reach_or_a_few_words_of_cheer(string text, bool reaction) =>
        Assert.Equal(reaction, CommentSkips.Reaction(text));

    [Theory]
    [InlineData("5m", 0)]
    [InlineData("now", 0)]
    [InlineData("3h", 3)]
    [InlineData("1d", 24)]
    [InlineData("2w", 336)]
    [InlineData("1mo", 720)]
    [InlineData("yesterday", null)]
    public void The_age_reads_as_LinkedIn_shows_it(string age, int? hours) => Assert.Equal(hours, CommentSkips.Hours(age));

    [Theory]
    [InlineData("SQL", "Postgres composite index ordering", null)]
    [InlineData("SQL", "EF Core pagination", null)]
    [InlineData(".NET", "marathon training", "his work does not tie in")]
    [InlineData("none", "Azure outage", "his work does not tie in")]
    public void A_claimed_fit_needs_a_word_of_his_areas(string fit, string topic, string? why) =>
        Assert.Equal(why, CommentSkips.Why(Comment(text: "The order you pick early decides how far the whole thing can go later.", fit: fit, topic: topic), 24));

    [Fact]
    public void A_seven_day_dry_window_keeps_what_a_day_drops()
    {
        Assert.Contains("older than 24 hours", CommentSkips.Why(Comment(age: "3d"), 24), StringComparison.Ordinal);
        Assert.Null(CommentSkips.Why(Comment(age: "3d"), 168));
        Assert.Contains("older than 168 hours", CommentSkips.Why(Comment(age: "2w"), 168), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("puts this into a live system.", "puts this into a live system")]
    [InlineData("Puts this into a live system", "puts this into a live system")]
    [InlineData("I've got a portfolio that runs on Azure", "runs on Azure")]
    [InlineData(".NET 10 runs end to end in", ".NET 10 runs end to end in")]
    [InlineData("does this \u2014 live", null)]
    [InlineData("lives at theyard.stevenstout.biz", null)]
    [InlineData("does this, right?", null)]
    [InlineData("tiny", null)]
    public void The_tie_is_one_plain_clause(string tie, string? clause) => Assert.Equal(clause, CommentPicks.Clause(tie));

    [Fact]
    public void The_line_is_the_convention_s_words_with_one_tagged_review_link()
    {
        Assert.Equal($"I've got a portfolio that {Tie}, worth a look: https://theyard.stevenstout.biz/?view=review&r=abc123", CommentPicks.Line(Tie, "abc123"));
    }

    [Fact]
    public void An_edit_counts_only_when_the_reload_shows_his_words_then_the_line_once()
    {
        OwnComment comment = Comment();
        var edit = new CommentEdit(1, comment, "abc123", CommentPicks.Line(Tie, "abc123"));
        Assert.Empty(CommentPicks.Missing(edit, $"{comment.Text}\n\n{edit.Line}"));
        Assert.Empty(CommentPicks.Missing(edit, $"{comment.Text}\n\n{edit.Line.Replace("https://", string.Empty, StringComparison.Ordinal)}"));
        Assert.Equal(["no read-back after the reload"], CommentPicks.Missing(edit, null));
        Assert.Contains("the code shows 2 times, not once", CommentPicks.Missing(edit, $"{comment.Text}\n\n{edit.Line}\n{edit.Line}"));
        Assert.Contains("his words did not read back as they were", CommentPicks.Missing(edit, $"We went back.\n\n{edit.Line}"));
        Assert.Contains("the line is not the comment's last", CommentPicks.Missing(edit, $"{comment.Text}\n\n{edit.Line}\nP.S."));
    }

    [Fact]
    public void The_read_run_reads_and_scrolls_and_the_edit_run_clicks_and_types_and_neither_fills()
    {
        var dry = ClaudeArguments.For(new LaneConfig(), Lane.Commentline, "/mnt/j", "/mnt/j/run", dry: true);
        var read = ClaudeArguments.For(new LaneConfig(), Lane.Commentline, "/mnt/j", "/mnt/j/run", dry: false);
        var edit = ClaudeArguments.CommentEdit(new LaneConfig(), "/mnt/j", "/mnt/j/run");
        Assert.DoesNotContain(dry, tool => tool.EndsWith("__computer", StringComparison.Ordinal));
        Assert.Contains(edit, tool => tool.EndsWith("__computer", StringComparison.Ordinal));
        foreach (var list in new[] { dry, read, edit })
        {
            Assert.DoesNotContain(list, tool => tool.EndsWith("__form_input", StringComparison.Ordinal) || tool.EndsWith("__file_upload", StringComparison.Ordinal));
            Assert.True(list.ToList().IndexOf(Chrome("browser_batch")) > list.ToList().IndexOf("--disallowedTools"));
            Assert.True(list.ToList().IndexOf(Chrome("javascript_tool")) > list.ToList().IndexOf("--disallowedTools"));
        }
        Assert.Contains("PHASE=read. WINDOW_HOURS=168", dry[1], StringComparison.Ordinal);
        Assert.Contains("PHASE=read. WINDOW_HOURS=24", read[1], StringComparison.Ordinal);
        Assert.Contains("do its section Edit and nothing else", edit[1], StringComparison.Ordinal);
        Assert.Contains("/mnt/j/run/edits.tsv", edit[1], StringComparison.Ordinal);
        Assert.Contains("DRY_RUN=false", edit[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_dry_run_reads_seven_days_lists_the_picks_mints_nothing_and_edits_nothing()
    {
        Morning morning = Played(_ => ResultLine.Empty, Comments("comments-30.json"));
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(true, Lane.Commentline), default);
        string done = morning.Share.Files[$"{Folder}-dry/DONE.md"];
        Assert.Single(morning.Claude.Requests);
        Assert.Empty(morning.Minter.Asked);
        Assert.Contains("for the last 168 hours; 30 passed the skip rules; the roll picked 10", done, StringComparison.Ordinal);
        Assert.Equal(10, done.Split('\n').Count(line => line.StartsWith("- Pick ", StringComparison.Ordinal) && line.Contains("r=[code]", StringComparison.Ordinal)));
        Assert.Equal(20, done.Split('\n').Count(line => line.EndsWith("not picked by the roll (10 of 30)", StringComparison.Ordinal)));
        Assert.Contains("commentline: dry, 10 picked, nothing edited, 20 left out", output, StringComparison.Ordinal);
        Assert.Empty(morning.TrackerRows());
    }

    [Fact]
    public async Task A_live_run_mints_a_code_a_pick_and_counts_an_edit_only_on_its_read_back()
    {
        Morning morning = null!;
        morning = Played(request =>
        {
            if (!request.StreamFile.EndsWith("stream-edit.jsonl", StringComparison.Ordinal))
            {
                return ResultLine.Empty;
            }
            var rows = morning.Share.Files[$"{Folder}/edits.tsv"].Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(line => line.Split('\t')).ToList();
            Assert.All(rows, cells => Assert.Equal(7, cells.Length));
            var results = rows.Select((cells, i) => new
            {
                row = int.Parse(cells[0]),
                outcome = "sent",
                reason = (string?)null,
                person = cells[2],
                title = (string?)null,
                profile_url = cells[1],
                evidence = new { pending = (bool?)null, toast = (string?)null, read_back = i == 0 ? "changed words\n\n" + cells[6] : cells[3] + "\n\n" + cells[6], confirmation = (string?)null, screenshot = (string?)null },
            });
            return ResultLine.With(JsonSerializer.Serialize(new { rows = results, live_invite_count = (int?)null, stop_condition = "none", stop_detail = (string?)null, stop_screenshot = (string?)null }));
        }, JsonSerializer.Serialize(new { comments = Enumerable.Range(0, 6).Select(n => Comment(text: $"Comment {n}: we went back to a modular monolith and deploys got boring again.")) }));
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Commentline), default);

        string done = morning.Share.Files[$"{Folder}/DONE.md"];
        Assert.Equal(2, morning.Claude.Requests.Count);
        Assert.Equal(["comment: modular monoliths", "comment: modular monoliths"], morning.Minter.Roles);
        Assert.Equal(["commentline", "commentline"], morning.Minter.Lanes);
        Assert.Contains("Edit 2: Marcus Hale, modular monoliths, code abc123: edited, verified on the reload.", done, StringComparison.Ordinal);
        Assert.Contains("DEFECT: Edit 1: Marcus Hale, modular monoliths, code abc123: claimed edited, UNVERIFIED: his words did not read back as they were", done, StringComparison.Ordinal);
        Assert.Contains("commentline: 1 of 2 edits verified, 4 left out", output, StringComparison.Ordinal);
        Assert.False(morning.Share.Files.ContainsKey(RunSlot.LinkedInClosed(new DateOnly(2026, 11, 3))));
        Assert.Empty(morning.TrackerRows());
    }

    [Fact]
    public async Task A_read_that_meets_a_checkpoint_mints_nothing_edits_nothing_and_closes_LinkedIn()
    {
        const string Checkpoint = """{"rows":[],"live_invite_count":null,"stop_condition":"checkpoint","stop_detail":"Let's do a quick security check","stop_screenshot":null}""";
        Morning morning = Played(_ => ResultLine.With(Checkpoint), Comments("comments-30.json"));
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Commentline), default);
        Assert.Single(morning.Claude.Requests);
        Assert.Empty(morning.Minter.Asked);
        Assert.Contains("checkpoint", morning.Share.Files[RunSlot.LinkedInClosed(new DateOnly(2026, 11, 3))], StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_edit_run_with_no_result_closes_LinkedIn_for_the_day()
    {
        Morning morning = Played(request => request.StreamFile.EndsWith("stream-edit.jsonl", StringComparison.Ordinal) ? null : ResultLine.Empty, Comments("comments-30.json"));
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Commentline), default);
        Assert.Equal(2, morning.Claude.Requests.Count);
        Assert.Contains("edit run gave no result", morning.Share.Files[RunSlot.LinkedInClosed(new DateOnly(2026, 11, 3))], StringComparison.Ordinal);
        Assert.Contains("commentline: 0 of 10 edits verified", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_driver_on_LinkedIn_holds_the_comment_line()
    {
        Morning morning = Played(_ => ResultLine.Empty, Comments("comments-30.json"));
        morning.Share.Files[RunSlot.LinkedInClosed(new DateOnly(2026, 11, 3))] = "the invites lane met weekly_limit\n";
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Commentline), default);
        Assert.Empty(morning.Claude.Requests);
        Assert.Contains("commentline: held, the invites lane met weekly_limit", output, StringComparison.Ordinal);
    }

    private static string Chrome(string tool) => "mcp__claude-in-chrome__" + tool;

    private static Morning Played(Func<ClaudeRequest, string?> result, string comments)
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            string folder = request.WorkingFolder["/mnt/j/".Length..];
            if (!request.StreamFile.EndsWith("stream-edit.jsonl", StringComparison.Ordinal))
            {
                morning.Share.Files[$"{folder}/{CommentPicks.ReadFile}"] = comments;
            }
            return new ClaudeRun(0, false, result(request));
        });
        morning.Config.Lanes["commentline"] = new LaneConfig { Model = "claude-opus-5-5" };
        return morning;
    }
}
