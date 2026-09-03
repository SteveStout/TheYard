using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TheYard.Infrastructure;

namespace TheYard.Tests;

/// <summary>
/// The SQL project is the authority and this is what enforces it
/// (ADR: Data first, and the database in source control).
///
/// `api/TheYard.Database` holds the schema as hand-written DDL and builds to a
/// DACPAC. Entity Framework maps to that schema: it does not create it on SQL
/// Server, it has no rights to alter it, and if the two disagree the .sql file
/// is right and the model is wrong. These tests read the .sql files, read the
/// EF model, and hold the second to the first, so "EF is a mapper" is a
/// property of the build rather than a sentence in a record.
///
/// No connection is opened. The model is built from the SQL Server provider and
/// read; the DDL is read off disk. Both are available on a CI runner with no
/// Azure credential, which is the only kind this project has.
/// </summary>
public class SchemaConformanceTests
{
    private static YardDbContext SqlServer() =>
        new(new YardConnection(YardProvider.SqlServer, "Server=none;Initial Catalog=none;").Options());

    // #region conformance
    [Fact]
    public void Every_table_the_model_maps_exists_in_the_sql_project()
    {
        var ddl = Ddl.Read();
        using var db = SqlServer();

        var missing = db.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName()!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(table => !ddl.Tables.ContainsKey(table))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"the model maps tables the SQL project does not declare: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Every_column_the_model_maps_has_the_type_the_sql_project_gives_it()
    {
        var ddl = Ddl.Read();
        using var db = SqlServer();
        var wrong = new List<string>();

        foreach (var entity in db.Model.GetEntityTypes())
        {
            string table = entity.GetTableName()!;
            if (!ddl.Tables.TryGetValue(table, out var declared))
            {
                continue;
            }

            var store = StoreObjectIdentifier.Create(entity, StoreObjectType.Table)!.Value;
            foreach (var property in entity.GetProperties())
            {
                string column = property.GetColumnName(store)!;
                if (!declared.Columns.TryGetValue(column, out var sql))
                {
                    wrong.Add($"{table}.{column} is mapped and not declared");
                    continue;
                }

                string mapped = Ddl.Normalize(property.GetColumnType());
                if (!string.Equals(mapped, sql.Type, StringComparison.Ordinal))
                {
                    wrong.Add($"{table}.{column}: the model says {mapped}, the SQL project says {sql.Type}");
                }

                if (property.IsNullable != sql.Nullable)
                {
                    wrong.Add(
                        $"{table}.{column}: the model says {(property.IsNullable ? "NULL" : "NOT NULL")}, "
                        + $"the SQL project says {(sql.Nullable ? "NULL" : "NOT NULL")}");
                }
            }
        }

        // The list, not the count. A conformance failure is only useful if it
        // names the column, because the fix is in one of two files and the
        // message is what says which.
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void Every_column_the_sql_project_declares_is_mapped()
    {
        var ddl = Ddl.Read();
        using var db = SqlServer();
        var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in db.Model.GetEntityTypes())
        {
            var store = StoreObjectIdentifier.Create(entity, StoreObjectType.Table)!.Value;
            foreach (var property in entity.GetProperties())
            {
                mapped.Add($"{entity.GetTableName()}.{property.GetColumnName(store)}");
            }
        }

        // A column nothing maps is a column nothing writes, which on a NOT NULL
        // column means every insert fails on the day it is added.
        var orphans = ddl.Tables
            .SelectMany(table => table.Value.Columns.Keys.Select(column => $"{table.Key}.{column}"))
            .Where(name => !mapped.Contains(name))
            .ToList();

        Assert.True(orphans.Count == 0, $"declared and unmapped: {string.Join(", ", orphans)}");
    }

    [Fact]
    public void Every_primary_key_agrees()
    {
        var ddl = Ddl.Read();
        using var db = SqlServer();
        var wrong = new List<string>();

        foreach (var entity in db.Model.GetEntityTypes())
        {
            string table = entity.GetTableName()!;
            if (!ddl.Tables.TryGetValue(table, out var declared))
            {
                continue;
            }

            var store = StoreObjectIdentifier.Create(entity, StoreObjectType.Table)!.Value;
            var modelKey = entity.FindPrimaryKey()!.Properties
                .Select(property => property.GetColumnName(store)!)
                .ToList();

            if (!modelKey.SequenceEqual(declared.PrimaryKey, StringComparer.OrdinalIgnoreCase))
            {
                wrong.Add(
                    $"{table}: the model keys on ({string.Join(", ", modelKey)}), "
                    + $"the SQL project keys on ({string.Join(", ", declared.PrimaryKey)})");
            }
        }

        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void Every_foreign_key_the_model_believes_in_is_declared()
    {
        var ddl = Ddl.Read();
        using var db = SqlServer();
        var wrong = new List<string>();

        foreach (var entity in db.Model.GetEntityTypes())
        {
            string table = entity.GetTableName()!;
            if (!ddl.Tables.TryGetValue(table, out var declared))
            {
                continue;
            }

            var store = StoreObjectIdentifier.Create(entity, StoreObjectType.Table)!.Value;
            foreach (var key in entity.GetForeignKeys())
            {
                string columns = string.Join(",", key.Properties.Select(property => property.GetColumnName(store)!));
                string principal = key.PrincipalEntityType.GetTableName()!;
                bool declaredHere = declared.ForeignKeys.Any(fk =>
                    string.Equals(fk.Columns, columns, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(fk.PrincipalTable, principal, StringComparison.OrdinalIgnoreCase));

                if (!declaredHere)
                {
                    wrong.Add($"{table}({columns}) to {principal} is in the model and not in the SQL project");
                }
            }
        }

        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void Every_index_the_model_believes_in_is_declared()
    {
        var ddl = Ddl.Read();
        using var db = SqlServer();
        var wrong = new List<string>();

        foreach (var entity in db.Model.GetEntityTypes())
        {
            string table = entity.GetTableName()!;
            if (!ddl.Tables.TryGetValue(table, out var declared))
            {
                continue;
            }

            var store = StoreObjectIdentifier.Create(entity, StoreObjectType.Table)!.Value;
            foreach (var index in entity.GetIndexes())
            {
                string columns = string.Join(",", index.Properties.Select(property => property.GetColumnName(store)!));
                bool declaredHere = declared.Indexes.Any(candidate =>
                    string.Equals(candidate.Columns, columns, StringComparison.OrdinalIgnoreCase)
                    && candidate.Unique == index.IsUnique);

                if (!declaredHere)
                {
                    wrong.Add($"{table}({columns}), unique {index.IsUnique}, is in the model and not in the SQL project");
                }
            }
        }

        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }
    // #endregion conformance

    // #region physical
    [Fact]
    public void The_catalogue_tables_are_clustered_on_the_column_they_are_read_in_order_of()
    {
        var ddl = Ddl.Read();

        // Physical design is the SQL project's to make and the model knows
        // nothing about it, which is the point: a clustered index is a storage
        // decision, and it belongs beside the storage. The only query either of
        // these tables serves is ORDER BY Seq, run once at startup, so that is
        // where the clustered index goes and the primary key is nonclustered.
        foreach (string table in new[] { "Vehicles", "Photos" })
        {
            var clustered = Assert.Single(ddl.Tables[table].Indexes, index => index.Clustered);
            Assert.Equal("Seq", clustered.Columns);
            Assert.True(clustered.Unique);
            Assert.False(ddl.Tables[table].PrimaryKeyClustered, $"{table}'s primary key should be nonclustered");
        }
    }

    [Fact]
    public void A_bid_carries_a_rowversion_and_no_second_copy_of_its_own_key()
    {
        var ddl = Ddl.Read();
        var bids = ddl.Tables["Bids"];

        Assert.Equal("rowversion", bids.Columns["RowVersion"].Type);

        // The primary key's leading column is UserId, so an index on UserId
        // alone would be a second copy of half the key: a write on every bid,
        // and nothing earned.
        Assert.DoesNotContain(bids.Indexes, index => index.Columns == "UserId");

        // And no foreign key to the catalogue, because a bid names a synthetic
        // vehicle id that has no row in Vehicles. The day the expansion is
        // persisted this is what fails and sends somebody to the record.
        Assert.DoesNotContain(bids.ForeignKeys, key => key.PrincipalTable == "Vehicles");
    }
    // #endregion physical
}

/// <summary>
/// A small reader for the DDL this repository writes. It is deliberately not a
/// T-SQL parser: it understands the shape the files in
/// `api/TheYard.Database` are written in, which is one CREATE TABLE per object
/// followed by its indexes, and it throws rather than guessing when it meets
/// something else. The build compiles the same files with the real thing, so
/// this only has to be right about what it reads, not about T-SQL.
/// </summary>
internal sealed class Ddl
{
    public Dictionary<string, DeclaredTable> Tables { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static Ddl Read()
    {
        string root = Path.Combine(Repo.Root(), "api", "TheYard.Database");
        var ddl = new Ddl();
        var files = Directory.GetFiles(root, "*.sql", SearchOption.AllDirectories);
        Assert.True(files.Length > 0, $"no .sql files under {root}");

        foreach (string file in files)
        {
            string text = StripComments(File.ReadAllText(file));
            foreach (Match table in TablePattern.Matches(text))
            {
                ddl.Tables[table.Groups["name"].Value] = ReadTable(table.Groups["body"].Value);
            }
        }

        // Indexes are separate statements and can name a table declared in
        // another file, so they are read after every table is known.
        foreach (string file in files)
        {
            string text = StripComments(File.ReadAllText(file));
            foreach (Match index in IndexPattern.Matches(text))
            {
                string table = index.Groups["table"].Value;
                Assert.True(ddl.Tables.ContainsKey(table), $"an index names {table}, which no file declares");
                ddl.Tables[table].Indexes.Add(new DeclaredIndex(
                    Columns: Columns(index.Groups["columns"].Value),
                    Unique: index.Groups["unique"].Success,
                    Clustered: index.Groups["clustered"].Success));
            }
        }

        return ddl;
    }

    /// <summary>Column types compare after their whitespace goes: `decimal(3, 1)` and `decimal(3,1)` are one type.</summary>
    public static string Normalize(string type) => type.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private static readonly Regex TablePattern = new(
        @"CREATE\s+TABLE\s+\[dbo\]\.\[(?<name>[^\]]+)\]\s*\((?<body>.*?)\)\s*;",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex IndexPattern = new(
        @"CREATE\s+(?<unique>UNIQUE\s+)?(?<clustered>CLUSTERED\s+)?INDEX\s+\[[^\]]+\]\s+ON\s+\[dbo\]\.\[(?<table>[^\]]+)\]\s*\((?<columns>[^)]*)\)",
        RegexOptions.IgnoreCase);

    private static readonly Regex ColumnPattern = new(
        @"^\[(?<name>[^\]]+)\]\s+(?<type>.+?)(?<null>\s+NOT\s+NULL|\s+NULL)(?<identity>\s+IDENTITY)?$",
        RegexOptions.IgnoreCase);

    private static readonly Regex PrimaryKeyPattern = new(
        @"CONSTRAINT\s+\[[^\]]+\]\s+PRIMARY\s+KEY\s*(?<clustering>NONCLUSTERED|CLUSTERED)?\s*\((?<columns>[^)]*)\)",
        RegexOptions.IgnoreCase);

    private static readonly Regex ForeignKeyPattern = new(
        @"CONSTRAINT\s+\[[^\]]+\]\s+FOREIGN\s+KEY\s*\((?<columns>[^)]*)\)\s+REFERENCES\s+\[dbo\]\.\[(?<principal>[^\]]+)\]",
        RegexOptions.IgnoreCase);

    private static DeclaredTable ReadTable(string body)
    {
        var table = new DeclaredTable();
        foreach (string item in SplitTopLevel(body))
        {
            string text = item.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (text.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase))
            {
                var key = PrimaryKeyPattern.Match(text);
                if (key.Success)
                {
                    table.PrimaryKey.AddRange(Columns(key.Groups["columns"].Value).Split(','));
                    table.PrimaryKeyClustered =
                        !string.Equals(key.Groups["clustering"].Value, "NONCLUSTERED", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                var foreign = ForeignKeyPattern.Match(text);
                Assert.True(foreign.Success, $"a constraint this reader does not understand: {text}");
                table.ForeignKeys.Add(new DeclaredForeignKey(
                    Columns(foreign.Groups["columns"].Value),
                    foreign.Groups["principal"].Value));
                continue;
            }

            var column = ColumnPattern.Match(text);
            Assert.True(column.Success, $"a column this reader does not understand: {text}");
            table.Columns[column.Groups["name"].Value] = new DeclaredColumn(
                Normalize(column.Groups["type"].Value),
                column.Groups["null"].Value.Trim().Equals("NULL", StringComparison.OrdinalIgnoreCase));
        }

        return table;
    }

    /// <summary>Commas inside `decimal(3,1)` do not separate columns, so depth is counted.</summary>
    private static IEnumerable<string> SplitTopLevel(string body)
    {
        int depth = 0;
        int start = 0;
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                yield return Collapse(body[start..i]);
                start = i + 1;
            }
        }
        yield return Collapse(body[start..]);
    }

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    private static string Columns(string list) =>
        string.Join(",", list.Split(',').Select(part => part.Trim().Trim('[', ']')));

    private static string StripComments(string sql) =>
        Regex.Replace(sql, @"--[^\n]*", string.Empty);
}

internal sealed class DeclaredTable
{
    public Dictionary<string, DeclaredColumn> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> PrimaryKey { get; } = [];

    public bool PrimaryKeyClustered { get; set; } = true;

    public List<DeclaredForeignKey> ForeignKeys { get; } = [];

    public List<DeclaredIndex> Indexes { get; } = [];
}

internal sealed record DeclaredColumn(string Type, bool Nullable);

internal sealed record DeclaredForeignKey(string Columns, string PrincipalTable);

internal sealed record DeclaredIndex(string Columns, bool Unique, bool Clustered);
