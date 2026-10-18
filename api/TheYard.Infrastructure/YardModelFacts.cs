using Microsoft.EntityFrameworkCore;

namespace TheYard.Infrastructure;

/// <summary>
/// What the relational model holds, read from the code and never from a database: the tables the
/// context maps and the newest migration in the schema's history, for the SQL card's "What runs on
/// it?" row (ADR: One frame for every resource). The context is built over SQLite in memory and no
/// connection is ever opened.
/// </summary>
public static class YardModelFacts
{
    /// <summary>The number of tables the model maps, and the newest migration's id, or "none".</summary>
    public static (int Tables, string Migration) Read()
    {
        var options = new DbContextOptionsBuilder<YardDbContext>()
            .UseSqlite("Data Source=:memory:", sqlite => sqlite.MigrationsAssembly(YardConnection.SqliteMigrations))
            .Options;
        using var context = new YardDbContext(options);
        int tables = context.Model.GetEntityTypes().Select(type => type.GetTableName()).Where(name => name is not null).Distinct().Count();
        string migration = context.Database.GetMigrations().LastOrDefault() ?? "none";
        return (tables, migration);
    }
}
