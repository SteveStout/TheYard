using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The tracker's SQL copy (PROMPT-JOBHUNT-TRACKER-SQL, rungs 1 and 2): the file cut into records and put back byte
/// for byte, over a checked-in sample of the real file's shapes with the names replaced (the byte order mark, the
/// header whose quoted Count cell carries more of them, both date shapes, an NA company, a joined company name, a
/// quote inside a field, and a line break inside a field both as LF and as CRLF), and the mirror that writes only
/// the rows whose hash changed and rebuilds the file from the copy.
/// </summary>
public sealed class TrackerSqlTests
{
    private static byte[] Sample() =>
        File.ReadAllBytes(Path.Combine(RootOf(), "functions", "jobhunt.Tests", "Fixtures", "tracker-shapes.csv"));

    [Fact]
    public void The_file_cut_into_records_is_put_back_byte_for_byte()
    {
        byte[] bytes = Sample();
        var file = TrackerFile.Split(bytes);
        Assert.True(file.Bom);
        Assert.True(file.EndsWithBreak);
        Assert.Equal(6, file.Records.Count);
        Assert.Contains("\n", file.Records[3], StringComparison.Ordinal);
        Assert.Contains("\r\n", file.Records[4], StringComparison.Ordinal);
        Assert.Equal(bytes, file.ToBytes());
        // Every record is one row of seven cells to the reader every lane uses.
        Assert.All(file.Records, record => Assert.Equal(7, Assert.Single(TrackerCsvText.Parse(record)).Length));
    }

    [Fact]
    public void A_file_with_no_last_break_and_no_mark_comes_back_the_same()
    {
        byte[] bytes = TrackerCsvText.Encode("Count,Person\r\n1,\"a\r\nb\"");
        var file = TrackerFile.Split(bytes);
        Assert.False(file.Bom);
        Assert.False(file.EndsWithBreak);
        Assert.Equal(2, file.Records.Count);
        Assert.Equal(bytes, file.ToBytes());
    }

    [Fact]
    public async Task The_mirror_writes_the_rows_that_changed_and_rebuilds_the_file()
    {
        using var scratch = new Scratch();
        byte[] bytes = Sample();
        await scratch.Store.WriteAsync(MergePaths.Master, bytes, CancellationToken.None);
        var table = new MemoryTrackerTable();
        var mirror = new TrackerMirror(scratch.Store, table);

        Assert.Equal(new TrackerCopied(5, 5), await mirror.CopyAsync(CancellationToken.None));
        Assert.Equal(new TrackerCopied(5, 0), await mirror.CopyAsync(CancellationToken.None));
        var parity = await mirror.ParityAsync(DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.Equal(new TrackerParity(true, 5, 5), parity);

        // One row changed and one appended: two rows written, and the copy is the file again.
        var file = TrackerFile.Split(bytes);
        var next = file with { Records = [.. file.Records.Take(2), file.Records[2].Replace("RECEIPT", "RECEIPT again", StringComparison.Ordinal), .. file.Records.Skip(3), "106,New Person,New Co,Email,10/25/2026,,new"] };
        await scratch.Store.WriteAsync(MergePaths.Master, next.ToBytes(), CancellationToken.None);
        Assert.Equal(new TrackerCopied(6, 2), await mirror.CopyAsync(CancellationToken.None));
        Assert.True((await mirror.ParityAsync(DateTimeOffset.UnixEpoch, CancellationToken.None)).Same);

        // A row taken off the end is taken off the copy.
        await scratch.Store.WriteAsync(MergePaths.Master, file.ToBytes(), CancellationToken.None);
        await mirror.CopyAsync(CancellationToken.None);
        Assert.Equal(new TrackerParity(true, 5, 5), await mirror.ParityAsync(DateTimeOffset.UnixEpoch, CancellationToken.None));
    }

    [Fact]
    public void The_parity_check_runs_on_the_first_pass_of_each_Central_day()
    {
        var noon = new DateTimeOffset(2026, 10, 25, 17, 0, 0, TimeSpan.Zero);
        Assert.True(TrackerMirror.ParityDue(null, noon));
        Assert.False(TrackerMirror.ParityDue(noon.AddHours(-3), noon));
        Assert.True(TrackerMirror.ParityDue(noon.AddDays(-1), noon));
    }

    [Fact]
    public void A_record_s_cells_are_the_seven_the_CSV_holds()
    {
        Assert.Equal(["102", "NA", "NA", "Email", "2026-10-02", "x", "a, b"], SqlTrackerTable.Cells("102,NA,NA,Email,2026-10-02,x,\"a, b\""));
        Assert.Equal(["1", "", "", "", "", "", ""], SqlTrackerTable.Cells("1"));
    }

    [Fact]
    public async Task An_unmoved_tracker_is_not_read_again()
    {
        using var scratch = new Scratch();
        byte[] bytes = Sample();
        string path = Path.Combine(scratch.Root, MergePaths.Master);
        await scratch.Store.WriteAsync(MergePaths.Master, bytes, CancellationToken.None);
        var table = new MemoryTrackerTable();
        var seen = new TrackerSeen();
        var mirror = new TrackerMirror(scratch.Store, table, seen);

        // The first pass reads the file and copies it; the second finds the same size and last write and reads nothing.
        Assert.Equal(new TrackerCopied(5, 5), await mirror.CopyAsync(CancellationToken.None));
        Assert.Equal(new TrackerCopied(5, 0), await mirror.CopyAsync(CancellationToken.None));
        Assert.Equal((1, 2), (scratch.Store.Reads, scratch.Store.Stats));

        // A write moves the last-write time, and the next pass reads the file whole and copies what changed.
        var file = TrackerFile.Split(bytes);
        var next = file with { Records = [.. file.Records, "106,New Person,New Co,Email,10/25/2026,,new"] };
        await scratch.Store.WriteAsync(MergePaths.Master, next.ToBytes(), CancellationToken.None);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));
        Assert.Equal(new TrackerCopied(6, 1), await mirror.CopyAsync(CancellationToken.None));
        Assert.Equal(2, scratch.Store.Reads);

        // The same size with a newer last write is read again, and writes nothing when the content did not change.
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));
        Assert.Equal(new TrackerCopied(6, 0), await mirror.CopyAsync(CancellationToken.None));
        Assert.Equal(3, scratch.Store.Reads);

        // A copy whose frame does not hold the hash the last pass read is never trusted: an empty copy reads the file.
        Assert.Equal(new TrackerCopied(6, 6), await new TrackerMirror(scratch.Store, new MemoryTrackerTable(), seen).CopyAsync(CancellationToken.None));
        Assert.Equal(4, scratch.Store.Reads);

        // The day's parity check still reads the whole file.
        Assert.True((await mirror.ParityAsync(DateTimeOffset.UnixEpoch, CancellationToken.None)).Same);
        Assert.Equal(5, scratch.Store.Reads);
    }

    /// <summary>
    /// The quiet pass (ADR-111, the addendum on the quiet pass): over a master whose size and last write have not moved, with
    /// the day's check done, a pass asks SQL nothing, and a new instance knows it from the line the last pass left on the
    /// share, without asking SQL either. A moved master, a new Central day or a line that does not read sends it to SQL.
    /// </summary>
    [Fact]
    public async Task A_quiet_pass_asks_SQL_nothing_and_a_new_instance_knows_it_from_the_share()
    {
        using var scratch = new Scratch();
        string path = Path.Combine(scratch.Root, MergePaths.Master);
        await scratch.Store.WriteAsync(MergePaths.Master, Sample(), CancellationToken.None);
        var noon = new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.Zero);
        var table = new MemoryTrackerTable();
        var first = new TrackerMirror(scratch.Store, table, new TrackerSeen());

        // Nothing is known yet: the pass goes to SQL, copies the file and leaves its line on the share.
        Assert.False(await first.QuietAsync(noon, CancellationToken.None));
        await first.CopyAsync(CancellationToken.None);
        await first.KeepSeenAsync(noon, CancellationToken.None);
        Assert.True(await scratch.Store.ExistsAsync(MergePaths.CopySeen, CancellationToken.None));
        int frames = table.Frames;

        // The same host, and then a new instance with an empty memory: both quiet, and neither asks for the frame.
        Assert.True(await first.QuietAsync(noon.AddMinutes(1), CancellationToken.None));
        var fresh = new TrackerSeen();
        Assert.True(await new TrackerMirror(scratch.Store, table, fresh).QuietAsync(noon.AddMinutes(2), CancellationToken.None));
        Assert.False(fresh.Empty);
        Assert.Equal(frames, table.Frames);

        // The next Central day's first pass checks the whole file, so it is not quiet.
        Assert.False(await first.QuietAsync(noon.AddDays(1), CancellationToken.None));

        // A write to the master moves its last write, and the pass goes to SQL.
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));
        Assert.False(await first.QuietAsync(noon.AddMinutes(3), CancellationToken.None));

        // A line that does not read is left alone: the instance stays empty and asks SQL.
        var garbled = new TrackerSeen();
        garbled.Load("not a line");
        Assert.True(garbled.Empty);
        Assert.Null(garbled.Text());
    }

    /// <summary>The copy in memory, the way SqlTrackerTable keeps it.</summary>
    private sealed class MemoryTrackerTable : ITrackerTable
    {
        private readonly SortedDictionary<int, string> _rows = [];
        private TrackerFrame? _frame;

        /// <summary>How many times the frame was asked for: the one SQL read every pass used to make.</summary>
        public int Frames { get; private set; }

        public Task<TrackerFrame?> FrameAsync(CancellationToken ct)
        {
            Frames++;
            return Task.FromResult(_frame);
        }

        public Task<IReadOnlyDictionary<int, string>> HashesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<int, string>>(_rows.ToDictionary(row => row.Key, row => TrackerFile.Hash(row.Value)));

        public Task KeepAsync(TrackerFile file, IReadOnlyList<int> positions, string fileHash, CancellationToken ct)
        {
            foreach (int position in positions)
            {
                _rows[position] = file.Records[position];
            }
            foreach (int extra in _rows.Keys.Where(position => position > file.Records.Count - 1).ToList())
            {
                _rows.Remove(extra);
            }
            _frame = new TrackerFrame(file.Records[0], file.Bom, file.EndsWithBreak, file.Records.Count - 1, fileHash, _frame?.ParityAt);
            return Task.CompletedTask;
        }

        public Task<TrackerFile?> RebuildAsync(CancellationToken ct) =>
            Task.FromResult(_frame is null ? null : new TrackerFile(_frame.Bom, [_frame.Record, .. _rows.Values], _frame.EndsWithBreak));

        public Task MarkParityAsync(bool same, DateTimeOffset at, CancellationToken ct)
        {
            _frame = _frame is null ? null : _frame with { ParityAt = at };
            return Task.CompletedTask;
        }
    }

    private static string RootOf()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "functions", "jobhunt.Tests", "Fixtures")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("functions/jobhunt.Tests/Fixtures not found above " + AppContext.BaseDirectory);
    }
}
