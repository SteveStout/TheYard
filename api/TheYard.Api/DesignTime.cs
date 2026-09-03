using Microsoft.EntityFrameworkCore.Design;
using TheYard.Infrastructure;

namespace TheYard.Api;

// #region design-time
/// <summary>
/// What `dotnet ef` uses when it needs the model. Without this the tooling boots
/// the application to find a context, which would run the migrate and seed block
/// against a schema that does not exist yet, on the one command whose whole job
/// is to create that schema.
///
/// It lives in the host rather than beside the context because `dotnet ef` looks
/// for a design-time factory in the startup project or in the project it is
/// writing migrations into, and this one has to serve a migrations project that
/// is neither.
///
/// Only SQLite has migrations. The SQL Server schema is the SQL project,
/// published by SqlPackage, and this application maps to it rather than building
/// it (ADR: Data first, and the database in source control), so there is nothing
/// for `dotnet ef` to write on that side:
///
///   dotnet ef migrations add Name --project api/TheYard.Migrations.Sqlite ^
///     --startup-project api/TheYard.Api -- --sqlite
///
/// The argument is still read, because the SQL Server model is what the schema
/// conformance test builds and it has to be reachable the same way:
///
///   dotnet ef dbcontext script --startup-project api/TheYard.Api -- --sqlserver
///
/// Everything after the bare `--` reaches this method as <paramref name="args"/>.
/// The connection strings here are design-time only: `dotnet ef` builds the
/// model from them, it does not connect.
/// </summary>
public sealed class YardDbContextFactory : IDesignTimeDbContextFactory<YardDbContext>
{
    public YardDbContext CreateDbContext(string[] args)
    {
        bool sqlServer = args.Any(arg => string.Equals(arg, "--sqlserver", StringComparison.OrdinalIgnoreCase));
        var connection = sqlServer
            ? new YardConnection(YardProvider.SqlServer, "Server=design-time;Database=design-time;")
            : new YardConnection(YardProvider.Sqlite, "Data Source=design-time.db");
        return new YardDbContext(connection.Options());
    }
}
// #endregion design-time
