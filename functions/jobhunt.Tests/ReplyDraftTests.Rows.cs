using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The reply pass's rows beside its files (datamove1123 version A1): every decision the files make is written as a row
/// in an in-memory row store, an id the files settled with no row gets one by reading through, a row that disagrees
/// with the files is counted, a row store that fails never stops the pass, and the table is asked for once while it is
/// there and once an hour while it is not. No database runs here.
/// </summary>
public sealed partial class ReplyDraftTests
{
    [Fact]
    public async Task Every_decision_the_files_make_is_written_as_a_row_and_the_files_still_decide()
    {
        using var scratch = new Scratch();
        scratch.Master(Tracker);
        var rows = new Rows();

        var pass = await new ReplyDrafts(scratch.Store, Mailbox(), null, new Fixed(Now)) { Rows = new ReplyDraftLedger(rows) }.RunAsync(CancellationToken.None);

        Assert.Equal(new ReplyDraftPass(6, 1, 1, 0, 0, 2, Rows: 3, Mismatch: 0, SqlError: 0), pass);
        Assert.True(scratch.Has("dispatch/ready/12-replydraft-m1.md"));
        Assert.True(scratch.Has("inbox-pass/reply-drafts/m2.skipped"));
        Assert.Equal(new ReplyDraftRow("m1", "t1", ReplyDraftRow.Queued), rows.Kept["m1"]);
        Assert.Equal(new ReplyDraftRow("m2", "t2", ReplyDraftRow.Skipped, "not a job thread"), rows.Kept["m2"]);
        Assert.Equal(ReplyDraftRow.Skipped, rows.Kept["m6"].Status);
        Assert.Equal(3, rows.Kept.Count);

        scratch.Write("inbox-pass/reply-drafts/m1.draft.md", "Dana,\n\nThursday works.\n\nSteve\n");
        var live = await new ReplyDrafts(scratch.Store, Mailbox(), new Drafts(), new Fixed(Now.AddMinutes(5))) { Rows = new ReplyDraftLedger(rows) }.RunAsync(CancellationToken.None);

        Assert.Equal((1, 3, 0, 0), (live.Placed, live.Rows, live.Mismatch, live.SqlError));
        Assert.Equal(new ReplyDraftRow("m1", "t1", ReplyDraftRow.Placed, null, "r-1"), rows.Kept["m1"]);
        Assert.Equal(Now.AddMinutes(5), rows.At["m1"]);
    }

    [Fact]
    public async Task An_id_the_files_settled_with_no_row_gets_its_row_and_a_row_that_disagrees_is_a_mismatch()
    {
        using var scratch = new Scratch();
        scratch.Master(Tracker);
        await new ReplyDrafts(scratch.Store, Mailbox(), null, new Fixed(Now)).RunAsync(CancellationToken.None);
        var rows = new Rows();

        var read = await new ReplyDrafts(scratch.Store, Mailbox(), null, new Fixed(Now.AddMinutes(5))) { Rows = new ReplyDraftLedger(rows) }.RunAsync(CancellationToken.None);

        Assert.Equal((0, 1, 3, 0), (read.Queued, read.Already, read.Rows, read.Mismatch));
        Assert.Equal(new ReplyDraftRow("m1", "t1", ReplyDraftRow.Queued), rows.Kept["m1"]);
        Assert.Equal(new ReplyDraftRow("m6", "t6", ReplyDraftRow.Skipped), rows.Kept["m6"]);

        rows.Kept["m1"] = rows.Kept["m1"] with { Status = ReplyDraftRow.Placed };
        rows.Kept["m4"] = new ReplyDraftRow("m4", "t4", ReplyDraftRow.Skipped);
        var again = await new ReplyDrafts(scratch.Store, Mailbox(), null, new Fixed(Now.AddMinutes(10))) { Rows = new ReplyDraftLedger(rows) }.RunAsync(CancellationToken.None);

        Assert.Equal((4, 2), (again.Rows, again.Mismatch));
        Assert.Equal(ReplyDraftRow.Placed, rows.Kept["m1"].Status);
    }

    [Fact]
    public async Task A_row_store_that_fails_never_stops_the_pass_and_is_counted_once()
    {
        using var scratch = new Scratch();
        scratch.Master(Tracker);
        var rows = new Rows { Fail = true };

        var pass = await new ReplyDrafts(scratch.Store, Mailbox(), null, new Fixed(Now)) { Rows = new ReplyDraftLedger(rows) }.RunAsync(CancellationToken.None);

        Assert.Equal(new ReplyDraftPass(6, 1, 1, 0, 0, 2, Rows: 0, Mismatch: 0, SqlError: 1), pass);
        Assert.Equal(1, rows.Calls);
        Assert.True(scratch.Has("dispatch/ready/12-replydraft-m1.md"));
        Assert.True(scratch.Has("inbox-pass/reply-drafts/m1.md"));
        Assert.True(scratch.Has("inbox-pass/reply-drafts/m6.skipped"));
    }

    [Fact]
    public void The_files_say_skipped_then_placed_then_stale_then_queued()
    {
        var names = new HashSet<string>(["a.md", "a.placed", "b.md", "b.stale", "c.md", "d.skipped", "e.draft.md"], StringComparer.Ordinal);
        Assert.Equal(
            [ReplyDraftRow.Placed, ReplyDraftRow.Stale, ReplyDraftRow.Queued, ReplyDraftRow.Skipped, null],
            new[] { "a", "b", "c", "d", "e" }.Select(id => ReplyDraftRow.FromFiles(id, names)));
    }

    [Fact]
    public async Task The_table_is_asked_for_once_while_it_is_there_and_once_an_hour_while_it_is_not()
    {
        int asked = 0;
        bool there = false;
        Task<bool> Ask(CancellationToken ct)
        {
            asked++;
            return Task.FromResult(there);
        }
        var table = new ReplyDraftSwitch(TimeSpan.FromHours(1));

        Assert.False(await table.PresentAsync(Ask, Now, CancellationToken.None));
        there = true;
        Assert.False(await table.PresentAsync(Ask, Now.AddMinutes(59), CancellationToken.None));
        Assert.Equal(1, asked);
        Assert.True(await table.PresentAsync(Ask, Now.AddMinutes(60), CancellationToken.None));
        there = false;
        Assert.True(await table.PresentAsync(Ask, Now.AddHours(5), CancellationToken.None));
        Assert.Equal(2, asked);
    }

    [Fact]
    public void Every_column_the_rows_name_is_declared_by_the_sql_project_and_the_table_holds_no_mail_text()
    {
        string table = File.ReadAllText(Path.Combine(Repo(), "functions", "JobHunt.Database", "Tables", "ReplyDrafts.sql"));
        var declared = TableColumn().Matches(table).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(["MessageId", "ThreadId", "Status", "Reason", "DraftId", "CreatedAt", "UpdatedAt"], declared);
        Assert.Contains("CHECK ([Status] IN ('skipped', 'queued', 'placed', 'stale'))", table, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX [IX_ReplyDrafts_Queued] ON [jobhunt].[ReplyDrafts] ([Status]) WHERE [Status] = 'queued';", table, StringComparison.Ordinal);
        Assert.Equal(["placed", "queued", "skipped", "stale"],
            typeof(ReplyDraftRow).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).Order(StringComparer.Ordinal));

        var statements = typeof(SqlReplyDraftRows).Assembly.GetType("JobHunt.Infrastructure.Features.Ats.ReplyDraftSql", throwOnError: true)!
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        var named = statements.SelectMany(sql => RowColumn().Matches(sql)).Select(m => m.Groups[1].Value)
            .Where(name => name is not ("jobhunt" or "ReplyDrafts" or "value")).Distinct().ToList();
        Assert.All(named, name => Assert.Contains(name, declared));
        Assert.Equal(3, statements.Count);
        Assert.Single(statements, sql => sql.Contains("INSERT", StringComparison.Ordinal) || sql.Contains("UPDATE", StringComparison.Ordinal));
        Assert.DoesNotContain(statements, sql => sql.Contains("DELETE", StringComparison.OrdinalIgnoreCase) || sql.Contains("TRUNCATE", StringComparison.OrdinalIgnoreCase));
    }

    private static string Repo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "functions", "JobHunt.Database")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("functions/JobHunt.Database not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"^\s*\[(\w+)\]\s+\w", RegexOptions.Multiline)]
    private static partial Regex TableColumn();

    [GeneratedRegex(@"\[(\w+)\]")]
    private static partial Regex RowColumn();

    /// <summary>The rows in memory, as the table would keep them; Fail makes every call throw as a lost database would.</summary>
    private sealed class Rows : IReplyDraftRows
    {
        public Dictionary<string, ReplyDraftRow> Kept { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, DateTimeOffset> At { get; } = new(StringComparer.Ordinal);

        public bool Fail { get; init; }

        public int Calls { get; private set; }

        public Task<bool> ReadyAsync(CancellationToken ct) => Task.FromResult(Called());

        public Task<IReadOnlyList<ReplyDraftRow>> FindAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
        {
            Called();
            return Task.FromResult<IReadOnlyList<ReplyDraftRow>>([.. ids.Where(Kept.ContainsKey).Select(id => Kept[id])]);
        }

        public Task UpsertAsync(ReplyDraftRow row, DateTimeOffset at, CancellationToken ct)
        {
            Called();
            Kept[row.MessageId] = Kept.TryGetValue(row.MessageId, out var old)
                ? row with { ThreadId = row.ThreadId ?? old.ThreadId, DraftId = row.DraftId ?? old.DraftId }
                : row;
            At[row.MessageId] = at;
            return Task.CompletedTask;
        }

        private bool Called()
        {
            Calls++;
            return Fail ? throw new InvalidOperationException("the database is gone") : true;
        }
    }
}
