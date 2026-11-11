using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The postings' database and the watchlist's inbox: every column a PostingsSql statement names is declared by the SQL
/// project, the four tables carry the columns PROMPT-JOBHUNT-POSTINGS names with their key and checks, nothing in the
/// statements deletes, and the boards inbox adds a board once, skips another kind and stops at the cap of four
/// hundred. No database runs here.
/// </summary>
public sealed partial class PostingsSqlTests
{
    private static string Root()
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

    private static string Table(string name) => File.ReadAllText(Path.Combine(Root(), "functions", "JobHunt.Database", "Tables", name + ".sql"));

    private static HashSet<string> Columns(string name) => Column().Matches(Table(name)).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<(string Name, string Sql)> Statements() =>
        typeof(SqlPostingBook).Assembly.GetType("JobHunt.Infrastructure.Features.Campaign.PostingsSql", throwOnError: true)!
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (field.Name, (string)field.GetRawConstantValue()!));

    [Fact]
    public void Every_column_a_statement_names_is_declared_by_the_sql_project()
    {
        var tables = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["Postings"] = Columns("Postings"), ["PostingChanges"] = Columns("PostingChanges"), ["PostingDescriptions"] = Columns("PostingDescriptions"), ["Companies"] = Columns("Companies"),
        };
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal) { ["p"] = "Postings", ["d"] = "PostingDescriptions", ["c"] = "Companies" };
        var wrong = new List<string>();
        foreach (var (name, sql) in Statements())
        {
            foreach (Match m in Qualified().Matches(sql).Where(m => aliases.ContainsKey(m.Groups[1].Value)))
            {
                if (!tables[aliases[m.Groups[1].Value]].Contains(m.Groups[2].Value))
                {
                    wrong.Add($"{name}: {m.Value}");
                }
            }
            foreach (Match target in Target().Matches(sql))
            {
                var declared = tables[target.Groups[2].Value];
                int end = sql.IndexOf(')', target.Index + target.Length);
                string list = sql[target.Index..(end < 0 ? sql.Length : end)];
                foreach (Match column in Bracketed().Matches(list).Where(c => c.Groups[1].Value is not ("jobhunt" or "campaign" or "value") && c.Groups[1].Value != target.Groups[2].Value))
                {
                    if (!declared.Contains(column.Groups[1].Value))
                    {
                        wrong.Add($"{name}: {target.Groups[2].Value}.{column.Groups[1].Value}");
                    }
                }
            }
        }
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
        Assert.Equal(12, Statements().Count());
        Assert.DoesNotContain(Statements(), s => s.Sql.Contains("DELETE", StringComparison.OrdinalIgnoreCase) || s.Sql.Contains("TRUNCATE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_four_tables_carry_the_columns_the_postings_prompt_names()
    {
        Assert.Superset(new HashSet<string>(["Source", "BoardToken", "PostingId", "CompanyKey", "Title", "Level", "RemoteStance", "Location", "CompMin", "CompMax", "CompCurrency", "CompInterval", "CompSource", "Url", "FirstSeenAt", "LastSeenAt", "ClosedAt", "ContentHash", "PublishedAt"]), Columns("Postings"));
        Assert.Superset(new HashSet<string>(["PostingKey", "Hash", "Text", "SeenAt"]), Columns("PostingDescriptions"));
        Assert.Superset(new HashSet<string>(["PostingKey", "Field", "OldValue", "NewValue", "ChangedAt"]), Columns("PostingChanges"));
        Assert.Superset(new HashSet<string>(["PostingKey", "Kind", "Value", "Source", "WrittenAt"]), Columns("PostingNotes"));
        Assert.Contains("CREATE UNIQUE INDEX [UX_Postings_Board] ON [jobhunt].[Postings] ([Source], [BoardToken], [PostingId])", Table("Postings"), StringComparison.Ordinal);
        Assert.Contains("[Kind] IN ('hiring_manager', 'applied', 'interview', 'outcome', 'note')", Table("PostingNotes"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_boards_inbox_reads_the_three_kinds_and_counts_the_rest()
    {
        const string tsv = "CompanyKey\tCompany\tBoardKind\tBoardToken\tSource\nacme.com\tAcme\tgreenhouse\tacme\tjob-boards.md\nacme.com\tAcme\tgreenhouse\tACME\tPRIORITY-BOARD.md\nglobex.com\tGlobex\tworkday\tglobex\tjob-boards.md\nnot a key\tX\tlever\tx\tjob-boards.md\ninitech.com\tInitech\tAshby\tinitech\tjob-boards.md\n";
        var (rows, other, unread) = BoardsInbox.Parse(tsv);
        Assert.Equal([("acme.com", "greenhouse", "acme"), ("initech.com", "ashby", "initech")], rows.Select(r => (r.CompanyKey, r.BoardKind, r.BoardToken)));
        Assert.Equal((1, 1), (other, unread));
    }

    [Fact]
    public async Task The_import_adds_each_board_once_stops_at_the_cap_and_moves_the_file()
    {
        using var scratch = new Scratch();
        scratch.Write("campaign/boards-inbox/20261105-1400-boards.tsv", "CompanyKey\tCompany\tBoardKind\tBoardToken\tSource\na.com\tA\tlever\ta\tx\nb.com\tB\tlever\tb\tx\nc.com\tC\tlever\tc\tx\nd.com\tD\tlever\td\tx\n");
        var watch = new Watch { Count = BoardsInbox.Cap - 2, Known = { "b" } };

        string line = await new BoardImport(scratch.Store, watch, TimeProvider.System).RunAsync(CancellationToken.None);

        Assert.Equal("1 board files, 2 boards added, 1 known, 0 other kinds, 0 unread, 1 over the cap of 400", line);
        Assert.Equal(["a", "b", "c"], watch.Asked);
        Assert.False(scratch.Has("campaign/boards-inbox/20261105-1400-boards.tsv"));
        Assert.Single(Directory.GetFiles(Path.Combine(scratch.Root, "campaign", "boards-inbox", "merged")));
    }

    private sealed class Watch : IWatchlist
    {
        public int Count { get; set; }

        public HashSet<string> Known { get; } = [];

        public List<string> Asked { get; } = [];

        public Task<int> BoardCountAsync(CancellationToken ct) => Task.FromResult(Count);

        public Task<bool> AddBoardAsync(BoardRow row, string run, DateTimeOffset at, CancellationToken ct)
        {
            Asked.Add(row.BoardToken);
            return Task.FromResult(!Known.Contains(row.BoardToken));
        }
    }

    [GeneratedRegex(@"^\s*\[(\w+)\]", RegexOptions.Multiline)]
    private static partial Regex Column();

    [GeneratedRegex(@"\b([pdc])\.\[(\w+)\]")]
    private static partial Regex Qualified();

    [GeneratedRegex(@"(?:INSERT INTO|UPDATE) \[(jobhunt|campaign)\]\.\[(\w+)\]")]
    private static partial Regex Target();

    [GeneratedRegex(@"\[(\w+)\]")]
    private static partial Regex Bracketed();
}
