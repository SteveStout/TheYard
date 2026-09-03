using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace TheBlock.Infrastructure;

/// <summary>
/// Which relational engine this process is talking to. Two, on purpose: Azure
/// SQL Database is where the deployed site keeps its data, and SQLite is what a
/// developer and a CI runner get, because neither of them has an Azure
/// credential and neither should need one
/// (ADR: The SQL Server backend).
/// </summary>
public enum YardProvider
{
    /// <summary>A file. Local development, every test, and the fallback if the cloud database is unreachable.</summary>
    Sqlite,

    /// <summary>Azure SQL Database, reached as a managed identity. What the deployed container uses.</summary>
    SqlServer,
}

/// <summary>
/// The one place that decides which database this process talks to and how it
/// is configured. Everything else takes the answer.
/// </summary>
public sealed record YardConnection(YardProvider Provider, string ConnectionString)
{
    /// <summary>
    /// Where the SQLite schema's history lives. SQL Server has no equivalent
    /// constant because it has no migrations: its schema is the SQL project,
    /// published by SqlPackage, and this application maps to it rather than
    /// building it (ADR: Data first, and the database in source control).
    /// </summary>
    public const string SqliteMigrations = "TheBlock.Migrations.Sqlite";

    // #region choose
    /// <summary>
    /// SQL Server when there is a SQL Server connection string to use, SQLite
    /// otherwise.
    ///
    /// The `__` test is not decoration. The container's SQL Server setting is
    /// substituted at roll time by the deploy, exactly as the Application
    /// Insights key is (ADR: Telemetry), and a substitution that fails leaves
    /// the literal placeholder `__YARD_SQL_CONNECTION__` behind. Treating a
    /// placeholder as absent means a broken deploy falls back to the SQLite
    /// path that shipped this site for a week, rather than crash-looping
    /// against a connection string that is not one.
    /// </summary>
    public static YardConnection Choose(string? sqlServer, string sqlite) =>
        !string.IsNullOrWhiteSpace(sqlServer) && !sqlServer.StartsWith("__", StringComparison.Ordinal)
            ? new YardConnection(YardProvider.SqlServer, sqlServer)
            : new YardConnection(YardProvider.Sqlite, sqlite);
    // #endregion choose

    // #region configure
    /// <summary>
    /// The provider, its migrations assembly, and on SQL Server the retry policy
    /// a cloud database needs.
    ///
    /// `EnableRetryOnFailure` is the difference between a working site and a
    /// crash loop here. The database is serverless and auto-pauses after an
    /// idle hour; the first connection after a pause wakes it, and while it is
    /// waking it answers with a transient error rather than a connection.
    ///
    /// The numbers are a budget, not a preference, and the two of them have to
    /// be read together. This runs during startup, before the container serves
    /// anything, and the deploy gives a new build five minutes to answer.
    ///
    /// A connect timeout is per attempt, and a connect timeout is transient, so
    /// the retry policy multiplies it. The worst case is
    /// (1 + maxRetryCount) attempts of <see cref="ConnectSeconds"/>, plus the
    /// backoff between them:
    ///
    ///   3 attempts x 60s + about 12s of backoff = about 3 minutes.
    ///
    /// That fits inside the deploy's five with the container's own start-up
    /// still to pay for. Sixty seconds an attempt covers a serverless resume,
    /// which takes thirty to sixty, and three attempts mean the second and
    /// third arrive at a database the first one already woke.
    ///
    /// The first version of this raised the connect timeout to ninety and left
    /// maxRetryCount at four, which is five attempts of ninety seconds plus
    /// backoff: about eight minutes, against a five minute deploy. A database
    /// that was genuinely gone would have turned an outage into a failed deploy
    /// as well, which is the exact failure the file-backed fallback exists to
    /// prevent (the staff review, 2026-09-03).
    /// </summary>
    public DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder) =>
        Provider == YardProvider.SqlServer
            ? builder.UseSqlServer(WithResumeBudget(ConnectionString), sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 2, maxRetryDelay: TimeSpan.FromSeconds(6), errorNumbersToAdd: null);
                sql.CommandTimeout(120);
            })
            : builder.UseSqlite(ConnectionString, sqlite => sqlite.MigrationsAssembly(SqliteMigrations));
    // #region resume-budget
    /// <summary>
    /// Give a paused serverless database time to wake up, by widening the
    /// connect timeout in code rather than in the setting.
    ///
    /// The first roll on Azure SQL failed here, and the exception said exactly
    /// where:
    ///
    ///   Connection Timeout Expired. The timeout period elapsed during the
    ///   post-login phase. [Pre-Login] initialization=135; handshake=432;
    ///   [Login] initialization=1; authentication=4; [Post-Login] complete=29347
    ///
    /// Read the phases. The handshake worked, and the login worked in four
    /// milliseconds, so the managed identity is fine and the firewall is fine.
    /// What ran out was post-login, at 29.3 seconds against SqlClient's default
    /// 30, which is what waking a paused database looks like: the free tier
    /// auto-pauses after an idle hour and a resume takes thirty to sixty
    /// seconds. A container rolls about once a day, so almost every deploy
    /// arrives at a database that is asleep.
    ///
    /// Sixty seconds an attempt, not thirty, and not five minutes. Sixty covers
    /// the documented resume range on its own, and the retry policy in
    /// <see cref="Configure"/> gives three attempts of it, so the total patience
    /// is three minutes and the arithmetic is written down in one place. A
    /// database that is genuinely gone still falls through to the file-backed
    /// path while the deploy is watching.
    ///
    /// In code rather than in the connection string because the connection
    /// string is a setting somebody edits under pressure, and this number has a
    /// reason that belongs next to it. An explicit Connect Timeout in the
    /// setting still wins: this only fills in a value that was never given.
    /// </summary>
    public const int ConnectSeconds = 60;

    public static string WithResumeBudget(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        // ShouldSerialize, not ContainsKey. A strongly typed connection string
        // builder knows every keyword the provider has, so ContainsKey answers
        // true for all of them whether or not anybody set one, and the first
        // version of this method used it and therefore never widened anything.
        // ShouldSerialize is the one that means "the caller supplied this".
        if (!builder.ShouldSerialize("Connect Timeout"))
        {
            builder.ConnectTimeout = ConnectSeconds;
        }

        return builder.ConnectionString;
    }
    // #endregion resume-budget

    // #endregion configure

    /// <summary>The options a context is built from, for callers that are not going through DI.</summary>
    public DbContextOptions<YardDbContext> Options()
    {
        var builder = new DbContextOptionsBuilder<YardDbContext>();
        Configure(builder);
        return builder.Options;
    }

    /// <summary>
    /// What this process is allowed to say about its database, in a log or on a
    /// public health endpoint: the engine, and nothing else.
    ///
    /// Not the connection string, not the server, not the database name. There
    /// is no password in this one to hide, because the deployed database has no
    /// SQL login at all, but "there is no secret in today's connection string"
    /// is a fact about today's configuration and not a property of the code that
    /// prints it. The next person to add a setting should not have to notice
    /// that this method would have printed it.
    /// </summary>
    public string Describe() => Provider switch
    {
        YardProvider.SqlServer => "Azure SQL Database",
        _ => "SQLite",
    };
}
