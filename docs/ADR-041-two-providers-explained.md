# ADR: Two providers and a SQL project, explained

Status: written 2026-09-03, for 1.0.0.49. This is the newcomer's record for the
database, the same way ADR: Program.cs, explained is the newcomer's record for
the host and ADR: Entity Framework, explained is for EF Core itself. If you have
written a web application and never had to think about where a schema comes from,
start here and then read ADR: The SQL Server backend and ADR: Data first for the
decisions.

Nothing here is new information. It is the same setup, told at the level of what
each piece is for.

## What is actually running

This application keeps four kinds of thing in a database: vehicles, photos,
accounts and bids. In the deployed site that database is **Azure SQL Database**,
which is Microsoft's managed SQL Server. On your laptop and in CI it is
**SQLite**, which is a database in a single file.

Two engines, one application, and one set of C# classes describing the data.
Nothing above `TheYard.Infrastructure` knows which one is underneath.

Why two? Because CI runs on a machine with no Azure credential and is never
getting one, and because a developer should be able to clone this and run
`dotnet test` without an Azure subscription. A test suite that needs a cloud
account is a test suite people stop running.

## What Entity Framework is doing here, and what it is not

EF Core is an **object-relational mapper**. You describe C# classes, it writes
the SQL. `db.Vehicles.OrderBy(row => row.Seq)` becomes
`SELECT ... FROM Vehicles ORDER BY Seq`.

EF can also **create** the database it maps to, from the same description. That
is the part this project deliberately does not use on SQL Server, and it is the
one thing worth understanding before anything else here makes sense.

On SQL Server, **the schema is written by hand as SQL** and lives in
`api/TheYard.Database`. EF maps to it. If the C# says a column is 64 characters
and the SQL says 32, the SQL is right and a test fails. The reason is in ADR:
Data first, and it is Steve's: a schema written as DDL survives a change of
framework, and a schema written as C# attributes does not.

On SQLite, EF still creates the schema, because a SQLite database here is made
and thrown away by the process that uses it and nothing else ever reads it.

## The vocabulary, in the order you will meet it

**A DbContext** is the class that represents a session with the database.
`YardDbContext` is this application's, it has a `DbSet` per table, and its
`OnModelCreating` is where the mapping is described. One context per unit of
work, created from a factory, disposed when the work is done.

**A provider** is the package that teaches EF a particular engine. This project
references two, `Microsoft.EntityFrameworkCore.SqlServer` and
`Microsoft.EntityFrameworkCore.Sqlite`, and picks between them at startup in
`YardConnection`.

**A migration** is a C# class holding the difference between two versions of a
schema, with an `Up` that applies it and a `Down` that reverses it. EF generates
them by comparing your model to a snapshot of what it looked like last time. They
are how a schema built from C# gets a history. Only SQLite has them here.

**A model snapshot** is the file EF keeps beside the migrations recording what
the model looked like after the last one. EF Core allows exactly one snapshot per
assembly, which is the whole reason the SQLite migrations sit in their own
project rather than beside the context: two providers produce two different
models, and two models cannot share one snapshot.

**A SQL project** is a project whose source files are `.sql` and whose output is
a **DACPAC**: a compiled description of a database. `dotnet build` on
`api/TheYard.Database` reads the `CREATE TABLE` files, checks that they make
sense together (a foreign key pointing at a table nobody declared is an error,
not a runtime surprise), and produces the package.

**SqlPackage** is the tool that takes a DACPAC and a live database, compares
them, and applies the difference. It is what publishes this schema to Azure, run
by a person rather than by the deploy.

**A value converter** is a rule that a property is one type in C# and another in
the database. `ConditionGrade` is a `double` in the record and a `decimal(3,1)`
in the table. `AuctionStart` is a `string` in the record and a `datetime2(0)` in
the table. The conversion happens on the way in and out, so the domain keeps the
shape it wants and the column keeps the type it should have.

**A concurrency token** is a column that changes every time the row does. When EF
updates a row it puts the token's old value in the `WHERE` clause, so if somebody
else changed the row in between, zero rows match and EF throws instead of
overwriting. On SQL Server the token is a `rowversion`, which the database
maintains itself. On SQLite the store sets a new value on every save, because
SQLite has no such type.

Without one you get a **lost update**: two people read a bid standing at $23,300,
both are told their bid is valid, and the lower one lands second and wins. On an
auction that is somebody's money.

**A clustered index** is the physical order of the rows in a table. There is one
per table, and SQL Server puts it on the primary key unless told otherwise. Here
it is on `Seq`, not on the primary key, because the only query these tables ever
serve is "give me every row in seed order" and that is the column that answers it.

**Managed identity authentication** means the connection string has no password
in it. The container asks Azure for a token as itself and hands that to the
database, which was configured to accept nothing else. There is no secret to
store, rotate, or leak. ADR: The SQL Server backend has the before and after.

## Where each thing lives

```
api/TheYard.Database/            the schema, hand-written SQL, the authority
  Tables/Vehicles.sql             one file per table, with the reason beside every length
  Tables/Identity/                the seven tables ASP.NET Core Identity expects
api/TheYard.Infrastructure/
  YardConnection.cs               which provider, which connection, and the retry policy
  YardDbContext.cs                the mapping, and the two lines SQL Server alone can express
  Rows.cs                         the storage row types, which are not the domain records
  EfSources.cs                    the adapters, the seed, and how a schema arrives per provider
api/TheYard.Migrations.Sqlite/   the SQLite schema's history
api/TheYard.Api/DesignTime.cs    what `dotnet ef` uses when it needs the model
api/TheYard.Tests/
  SchemaConformanceTests.cs       holds the mapping to the SQL project
  SqlServerModelTests.cs          the mapping's own rules, asserted without a database
  RelationalConstraintTests.cs    the foreign key and the token, against a real engine
```

## How to add a column

In this order, because the tests enforce it.

1. **Add it to the SQL file.** `api/TheYard.Database/Tables/Vehicles.sql`, with
   a type and a length and a comment saying why that length.
2. **Add it to the row type**, `Rows.cs`.
3. **Map it** in `YardDbContext.OnModelCreating` if it needs a length, a type or
   a converter.
4. **Add it to the domain record and the mapping** in `Vehicle.cs` and
   `VehicleRows`, if the application is going to read it.
5. **Add a SQLite migration**, so local and CI get the column too:

   ```
   dotnet ef migrations add AddedTheColumn ^
     --project api/TheYard.Migrations.Sqlite ^
     --startup-project api/TheYard.Api ^
     --context YardDbContext -- --sqlite
   ```

6. **Run the tests.** `SchemaConformanceTests` fails if the SQL and the mapping
   disagree, and names the column.
7. **Publish the schema** to Azure when it ships:

   ```
   dotnet build api/TheYard.Database/TheYard.Database.sqlproj
   sqlpackage /Action:Publish /SourceFile:api/TheYard.Database/bin/Debug/TheYard.Database.dacpac ^
     /TargetConnectionString:"Server=tcp:...;Initial Catalog=sqldb-theyard-ss;Authentication=Active Directory Default;Encrypt=True;"
   ```

If you do steps 2 and 3 without step 1, the build fails and tells you so. That is
the order being enforced rather than remembered.

## What happens when you run it locally

Nothing about Azure. `ConnectionStrings:YardSql` is not set, so `YardConnection`
picks SQLite; `ConnectionStrings:Yard` is not set either, so the process makes a
scratch file in the temp directory, migrates it, seeds it from
`data/vehicles.json`, and deletes it on the way out. `dotnet test` does the same
thing once per test class.

## What happens when the container starts

`YardConnection` sees a SQL Server connection string, so EF is pointed at Azure.
`YardDatabase.Prepare` asks the database whether the four tables it maps are
there. If they are, it seeds any that are empty and the site runs on SQL Server.
If they are not, or the database is asleep and does not wake inside the retries,
`Prepare` returns "not ready", the composition root registers the JSON file
readers instead, and the site serves the catalogue read-only and says so on
`/api/health`. Nothing 500s and nothing crash-loops.

That last paragraph is the one worth remembering. A cloud database on a free tier
auto-pauses, and a site that falls over when its database naps is worse than the
file it replaced.

## Files

- [`api/TheYard.Infrastructure/YardConnection.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/YardConnection.cs)
- [`api/TheYard.Infrastructure/YardDbContext.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/YardDbContext.cs)
- [`api/TheYard.Api/DesignTime.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/DesignTime.cs)
- [`api/TheYard.Database`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Database)
- [`docs/ADR-039-sql-server-backend.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-039-sql-server-backend.md)
- [`docs/ADR-040-database-source-control.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-040-database-source-control.md)
- [`docs/ADR-034-entity-framework-explained.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-034-entity-framework-explained.md)
