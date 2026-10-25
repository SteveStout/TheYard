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

    /// <summary>The copy in memory, the way SqlTrackerTable keeps it.</summary>
    private sealed class MemoryTrackerTable : ITrackerTable
    {
        private readonly SortedDictionary<int, string> _rows = [];
        private TrackerFrame? _frame;

        public Task<TrackerFrame?> FrameAsync(CancellationToken ct) => Task.FromResult(_frame);

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
