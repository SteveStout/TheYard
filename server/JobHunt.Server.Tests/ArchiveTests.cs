namespace JobHunt.Server.Tests;

/// <summary>
/// The archive job against the share in memory and an archiver that records what it is asked: what is old enough,
/// at most five of a kind oldest first, a nightly day refused while a run in it is unsettled or a live document names
/// it, nothing touched while a cost1123 row is open, a zip that does not verify set aside with its source left where it
/// was, the log line before the move, the note where mentor/logs was, and the daily line naming what it could not do.
/// </summary>
public sealed class ArchiveTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 9, 40, 0, TimeSpan.Zero);

    private static (MemoryShare Share, RecordingArchiver Archiver, ArchiveRun Run) Job(Action<MemoryShare>? setup = null)
    {
        var share = new MemoryShare();
        share.Files["state.md"] = "## LANE: other1123 LIVE | IN PROGRESS |\n<!-- AID:20261123-120000Z-other1123-registry -->\n";
        setup?.Invoke(share);
        var archiver = new RecordingArchiver(share);
        return (share, archiver, new ArchiveRun(share, archiver, new FixedTime(Now)));
    }

    private static void Day(MemoryShare share, string day, string run = "0600-invites-sonnet-5-5", string done = "# done\n")
    {
        share.Files[$"nightly/{day}/{run}/DONE.md"] = done;
        share.Files[$"nightly/{day}/{run}/stream.jsonl"] = "{}";
    }

    [Theory]
    [InlineData("2026-11-08", true)]
    [InlineData("2026-11-09", false)]
    [InlineData("2026-11-23", false)]
    public void A_day_folder_is_old_enough_once_it_is_more_than_fourteen_central_days_back(string day, bool old) =>
        Assert.Equal(old, ArchiveUnits.OldDay(DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture), new DateOnly(2026, 11, 23)));

    [Fact]
    public void A_lock_file_counts_only_when_released_or_dead_and_goes_in_one_zip_a_month()
    {
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794236972), ArchiveUnits.LockStamp("chrome.dead-1794236972"));
        Assert.Null(ArchiveUnits.LockStamp("theyard-gate.lock"));
        Assert.Null(ArchiveUnits.LockStamp("README.md"));
        Assert.Null(ArchiveUnits.LockStamp("_backup_locks-README_pre-g1.md"));
        var units = ArchiveUnits.LockUnits(["chrome.dead-1792000000", "a.released-1792100000", "b.released-1794100000", "c.released-1795066646", "x.lock", "README.md"], Now);
        Assert.Equal(["2026-10", "2026-11"], units.Select(u => u.Name));
        Assert.Equal(["a.released-1792100000", "chrome.dead-1792000000"], units[0].Files);
        Assert.Equal(["b.released-1794100000"], units[1].Files);
        Assert.All(units, u => Assert.Equal("dispatch/locks", u.Source));
    }

    [Fact]
    public void A_folded_link_token_file_waits_thirty_days_and_a_live_document_names_a_day_either_way_round()
    {
        var units = ArchiveUnits.MergedUnits([("old.csv", Now.AddDays(-31)), ("new.csv", Now.AddDays(-29))], Now);
        Assert.Equal(["old.csv"], Assert.Single(units).Files);
        Assert.Equal([new DateOnly(2026, 11, 9), new DateOnly(2026, 10, 30)], ArchiveUnits.NamedDays(@"read J:\nightly\2026-11-09\ah-replycheck\ and /mnt/j/nightly/2026-10-30/x"));
        Assert.True(ArchiveUnits.LiveDocument("NIGHTLY-LANE.md"));
        Assert.False(ArchiveUnits.LiveDocument("_backup_NIGHTLY-LANE.md"));
        Assert.False(ArchiveUnits.LiveDocument("jobs-candidates-2026-11-01.tsv"));
    }

    [Fact]
    public async Task The_daily_pass_takes_five_settled_days_oldest_first_and_refuses_an_unsettled_or_named_one()
    {
        var (share, archiver, run) = Job(share =>
        {
            foreach (string day in new[] { "2026-10-20", "2026-10-21", "2026-10-22", "2026-10-23", "2026-10-24", "2026-10-25", "2026-10-26", "2026-10-27", "2026-11-09" })
            {
                Day(share, day);
            }
            Day(share, "2026-10-21", "0600-apply-opus-5-5", "Rows\nDEFECT: row 3 not written\n");
            Day(share, "2026-10-22", "0600-messages-sonnet-5-5", "# done\nHELD: a hold on LinkedIn\n");
            share.Files["nightly/2026-10-23/0600-feed-opus-5-5/progress.tsv"] = "row\n";
            share.Files["nightly/2026-10-24/0600-feed-opus-5-5-dry/progress.tsv"] = "row\n";
            share.Files["nightly/NIGHTLY-LANE.md"] = @"Standing read: J:\nightly\2026-10-25\ah-replycheck\";
            share.Files["nightly/_backup_NIGHTLY-LANE.md"] = "nightly/2026-10-20/";
        });

        var (exit, output) = await run.ExecuteAsync(new ArchiveOptions(false, null), CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(["nightly/2026-10-20", "nightly/2026-10-24", "nightly/2026-10-26", "nightly/2026-10-27"], archiver.Zipped);
        Assert.Contains("nightly/2026-10-21 (0600-apply-opus-5-5/DONE.md carries a defect line", output, StringComparison.Ordinal);
        Assert.Contains("nightly/2026-10-22 (0600-messages-sonnet-5-5/DONE.md says HELD)", output, StringComparison.Ordinal);
        Assert.Contains("nightly/2026-10-23 (0600-feed-opus-5-5/progress.tsv has no DONE note)", output, StringComparison.Ordinal);
        Assert.Contains("nightly/2026-10-25 (nightly/NIGHTLY-LANE.md names it)", output, StringComparison.Ordinal);
        Assert.Contains("4 archived, 0 failed, 4 refused", output, StringComparison.Ordinal);
        Assert.True(share.Files.ContainsKey("_to_delete/datamove1123/nightly/2026-10-20/0600-invites-sonnet-5-5/DONE.md"));
        Assert.False(share.Files.ContainsKey("nightly/2026-10-20/0600-invites-sonnet-5-5/DONE.md"));
        Assert.True(share.Files.ContainsKey("nightly/2026-11-09/0600-invites-sonnet-5-5/DONE.md"));
        string log = share.Files[ArchiveUnits.LogPath];
        Assert.StartsWith(ArchiveUnits.LogHeader, log, StringComparison.Ordinal);
        Assert.Contains("\tnightly/2026-10-20\t2\t", log, StringComparison.Ordinal);
        string note = Assert.Single(share.Files, f => f.Key.StartsWith("inbox/append-inbox/state.md/", StringComparison.Ordinal)).Value;
        Assert.StartsWith("## LANE: jobhunt-archive NOTE ", note, StringComparison.Ordinal);
        Assert.EndsWith("| NOTE |\n", note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task At_most_five_of_a_kind_go_in_a_run_and_the_rest_wait()
    {
        var (_, archiver, run) = Job(share =>
        {
            for (int d = 1; d <= 7; d++)
            {
                Day(share, $"2026-10-{d:00}");
                share.Files[$"dispatch/done/2026-10-{d:00}/x-DONE.md"] = "done";
            }
            share.Files["dispatch/locks/README.md"] = "x";
            share.Files["dispatch/locks/chrome.dead-1792000000"] = "x";
            share.Files["inbox/link-tokens/merged/a.csv"] = "x";
        });

        var (_, output) = await run.ExecuteAsync(new ArchiveOptions(false, null), CancellationToken.None);

        Assert.Equal(5, archiver.Zipped.Count(z => z.StartsWith("nightly/", StringComparison.Ordinal)));
        Assert.Equal(5, archiver.Zipped.Count(z => z.StartsWith("dispatch/done/", StringComparison.Ordinal)));
        Assert.Contains("dispatch/locks/dispatch-locks:2026-10 (1 files)", archiver.Zipped);
        Assert.DoesNotContain(archiver.Zipped, z => z.StartsWith("inbox/link-tokens/merged/", StringComparison.Ordinal));
        Assert.Contains("waiting: 2 more nightly days for the next runs", output, StringComparison.Ordinal);
        Assert.Contains("waiting: 2 more dispatch done days for the next runs", output, StringComparison.Ordinal);
        Assert.Equal("nightly/2026-10-01", archiver.Zipped[0]);
    }

    [Fact]
    public async Task A_dry_run_names_the_units_and_reads_and_moves_nothing()
    {
        var (share, archiver, run) = Job(share =>
        {
            Day(share, "2026-10-20", done: "DEFECT: x\n");
            share.Files["nightly/NIGHTLY-LANE.md"] = "nightly/2026-10-20/";
        });
        int files = share.Files.Count;

        var (exit, output) = await run.ExecuteAsync(new ArchiveOptions(true, null), CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("would archive nightly/2026-10-20 -> _archive/datamove1123/nightly/2026-10-20.zip", output, StringComparison.Ordinal);
        Assert.Contains("dry run: 1 units named", output, StringComparison.Ordinal);
        Assert.Empty(archiver.Zipped);
        Assert.Equal(0, archiver.Calls);
        Assert.Equal(files, share.Files.Count);
    }

    [Fact]
    public async Task Nothing_is_listed_while_a_cost1123_row_is_open_and_a_row_older_than_two_days_holds_nothing()
    {
        const string open = "## LANE: cost1123 LIVE from 10:00 | IN PROGRESS |\n<!-- AID:20261123-080000Z-cost1123-registry -->\n";
        var (share, archiver, run) = Job(share => Day(share, "2026-10-20"));
        share.Files["state.md"] += open;

        var (exit, output) = await run.ExecuteAsync(new ArchiveOptions(false, null), CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("held, a cost1123 row is open", output, StringComparison.Ordinal);
        Assert.Empty(archiver.Zipped);

        share.Files["state.md"] += "## LANE: cost1123 CLOSED 12:00. Done. | CLOSED |\n<!-- AID:20261123-090000Z-cost1123-close -->\n";
        Assert.DoesNotContain("held", (await run.ExecuteAsync(new ArchiveOptions(true, null), CancellationToken.None)).Output, StringComparison.Ordinal);
        Assert.False(RegistryTail.IsOpen(open.Replace("20261123-080000Z", "20261121-090000Z", StringComparison.Ordinal), "cost1123", Now, ArchiveRun.HolderCeiling));
        Assert.True(RegistryTail.IsOpen(open, "cost1123", Now.AddHours(6), ArchiveRun.HolderCeiling));
        Assert.False(RegistryTail.IsOpen(open, "cost1124", Now.AddHours(6), ArchiveRun.HolderCeiling));
    }

    [Fact]
    public async Task A_zip_that_does_not_verify_is_set_aside_and_its_source_stays_where_it_was()
    {
        var (share, archiver, run) = Job(share => Day(share, "2026-10-20"));
        archiver.Corrupt = true;

        var (exit, output) = await run.ExecuteAsync(new ArchiveOptions(false, null), CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("failed nightly/2026-10-20: nightly/2026-10-20/0600-invites-sonnet-5-5/DONE.md reads", output, StringComparison.Ordinal);
        Assert.Equal(["_archive/datamove1123/nightly/2026-10-20.zip"], archiver.Failed);
        Assert.True(share.Files.ContainsKey("nightly/2026-10-20/0600-invites-sonnet-5-5/DONE.md"));
        Assert.False(share.Files.ContainsKey(ArchiveUnits.LogPath));
    }

    [Fact]
    public async Task Mentor_logs_goes_once_by_name_leaves_a_note_and_the_daily_line_names_it_when_it_comes_back()
    {
        var (share, archiver, run) = Job(share =>
        {
            share.Files["mentor/logs/a.log"] = "a";
            share.Files["mentor/logs/old/b.log"] = "b";
        });
        Assert.DoesNotContain("mentor/logs", (await run.ExecuteAsync(new ArchiveOptions(false, null), CancellationToken.None)).Output, StringComparison.Ordinal);

        var (exit, output) = await run.ExecuteAsync(new ArchiveOptions(false, "mentor/logs"), CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("archived mentor/logs: 2 files", output, StringComparison.Ordinal);
        string note = share.Files[WritePolicy.LogsMoved];
        Assert.Contains(@"J:\_archive\datamove1123\mentor\logs.zip", note, StringComparison.Ordinal);
        Assert.Contains(@"J:\_to_delete\datamove1123\mentor\logs", note, StringComparison.Ordinal);
        Assert.StartsWith("> RULES FIRST: read systemRules.md", note, StringComparison.Ordinal);
        Assert.Contains("skipped mentor/logs: it holds MOVED.md", (await run.ExecuteAsync(new ArchiveOptions(false, "mentor/logs"), CancellationToken.None)).Output, StringComparison.Ordinal);

        share.Files["mentor/logs/new.log"] = "back";
        Assert.Contains("Came back: mentor/logs holds 1 entries beside MOVED.md, the first new.log", (await run.ExecuteAsync(new ArchiveOptions(false, null), CancellationToken.None)).Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_named_unit_too_young_is_skipped_an_unknown_one_refused_and_a_run_past_its_calls_leaves_the_rest()
    {
        var (_, archiver, run) = Job(share =>
        {
            Day(share, "2026-11-20");
            Day(share, "2026-10-01");
            Day(share, "2026-10-02");
        });

        var young = await run.ExecuteAsync(new ArchiveOptions(false, "nightly/2026-11-20"), CancellationToken.None);
        Assert.Equal(0, young.Exit);
        Assert.Contains("skipped nightly/2026-11-20: 3 days old", young.Output, StringComparison.Ordinal);
        Assert.Equal(2, (await run.ExecuteAsync(new ArchiveOptions(false, "queue"), CancellationToken.None)).Exit);
        archiver.Calls = ArchiveUnits.MostCalls - 10;
        var (_, output) = await run.ExecuteAsync(new ArchiveOptions(false, null), CancellationToken.None);
        Assert.Contains("left for tomorrow: nightly/2026-10-01", output, StringComparison.Ordinal);
        Assert.Empty(archiver.Zipped);
        Assert.Null(ArchiveOptions.Parse(["archive", "--unit"]));
        Assert.Equal(new ArchiveOptions(true, "mentor/logs"), ArchiveOptions.Parse(["archive", "--dry-run", "--unit", @"mentor\logs"]));
    }

    /// <summary>An archiver over the share in memory that records each unit it zips and moves keys as a rename would.</summary>
    private sealed class RecordingArchiver(MemoryShare share) : IArchiver
    {
        public List<string> Zipped { get; } = [];

        public List<string> Failed { get; } = [];

        public bool Corrupt { get; set; }

        public int Calls { get; set; }

        public Task<int> CountAsync(ArchiveUnit unit, CancellationToken ct) => Task.FromResult(Paths(unit).Count);

        public Task<IReadOnlyList<ArchiveEntry>> ReadAsync(ArchiveUnit unit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ArchiveEntry>>([.. Paths(unit).Select(path => new ArchiveEntry(path, share.Files[path].Length, 7))]);

        public Task<ArchiveZip> ZipAsync(ArchiveUnit unit, string zip, CancellationToken ct)
        {
            Zipped.Add(unit.Label);
            return Task.FromResult(new ArchiveZip(zip, 100, new string('a', 64)));
        }

        public async Task<IReadOnlyList<ArchiveEntry>> OpenAsync(string zip, CancellationToken ct)
        {
            string label = Zipped[^1];
            var entries = await ReadAsync(Unit(label), ct);
            return Corrupt ? [.. entries.Select(e => e with { Crc = 8 })] : entries;
        }

        public Task<string> MoveAsync(ArchiveUnit unit, string target, CancellationToken ct)
        {
            foreach (string path in Paths(unit))
            {
                string rest = unit.IsFiles ? path[(unit.Source.Length + 1)..] : $"{unit.Name}{path[unit.Source.Length..]}";
                share.Files[$"{target}/{rest}"] = share.Files[path];
                share.Files.Remove(path);
            }
            return Task.FromResult(unit.IsFiles ? $"{target}/" : $"{target}/{unit.Name}");
        }

        public Task<string> FailAsync(string zip, CancellationToken ct)
        {
            Failed.Add(zip);
            return Task.FromResult(zip + ".failed");
        }

        private readonly Dictionary<string, ArchiveUnit> units = new(StringComparer.Ordinal);

        private ArchiveUnit Unit(string label) => units[label];

        private List<string> Paths(ArchiveUnit unit)
        {
            units[unit.Label] = unit;
            return unit.IsFiles
                ? [.. unit.Files.Select(name => $"{unit.Source}/{name}").Where(share.Files.ContainsKey)]
                : [.. share.Files.Keys.Where(path => path.StartsWith(unit.Source + "/", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
        }
    }
}
