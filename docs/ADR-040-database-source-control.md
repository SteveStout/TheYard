# ADR: Data first, and the database in source control

Status: accepted, 2026-09-03, shipped as 1.0.0.49. Steve's ask, in his words:
"we are doing data first for entity framework and if needed we'll create a data
project or a source control of the database, but the database must be source
controlled", and then, when the two branches were priced: "I like the SQL project
first and entity as a mapper as that way if you decide to change technologies you
still keep your data structure."

## The decision

**`api/TheYard.Database` is the authority for the SQL Server schema.** It is a
SQL project: hand-written `CREATE TABLE` files, one per object, that `dotnet
build` compiles into a DACPAC. Entity Framework maps to what is in there. It does
not create it, it cannot alter it, and when the two disagree the `.sql` file is
right.

The alternative was on the table and was rejected with a reason. Model-first with
EF migrations, plus a generated `schema.sql` checked in and a drift test, would
have shipped hours earlier and would have satisfied the literal words "the
database must be source controlled". Steve's reason for the other branch is the
one that decides it: the data structure outlives the framework that reads it. A
schema expressed as C# attributes and a chain of migration classes is portable to
exactly one technology. A schema expressed as DDL is portable to anything that
speaks SQL, and it is reviewable by people who do not read C#.

## What it costs, said plainly

**Two files change when a column changes**, and in this order: the `.sql` file
first, then the mapping. The conformance test is what makes the order stick,
because a mapping that runs ahead of the DDL fails the build.

**Identity's tables had to be transcribed.** ASP.NET Core Identity generates
seven tables and this project now declares them by hand, which means an Identity
upgrade that changes a column is a change this repository has to make rather than
one a migration makes for it. The transcription came from EF's own
`GenerateCreateScript` output, so it started correct, and the conformance test is
what keeps it correct.

**The row types stay hand-shaped rather than scaffolded.** The usual
database-first workflow scaffolds entity classes from the schema, which would
have overwritten `Rows.cs` and undone the separation ADR: The relational store
built on purpose: the domain record is not the storage row, and the storage
layer does not get to reshape the domain. So this is database-first in the sense
that matters, the schema is the authority, and not in the sense that generates
code. The conformance test is what replaces the generator.

## The authority, and the one chain to it

```
api/TheYard.Database/Tables/*.sql        the authority
        |  SchemaConformanceTests
        v
YardDbContext (SQL Server model)          the mapping, held to the authority by a test
        |  the same OnModelCreating, minus what SQL Server alone can express
        v
SQLite, created by EF migrations          local development and CI
```

There is one authority and the chain to it is testable at every hop. That is the
answer to the "two authorities" objection, which is a real one: a repository that
holds DDL and migrations and lets both create a schema has two definitions of the
same table and no way to tell which one the database in front of you came from.

## What enforces it

```live path=api/TheYard.Tests/SchemaConformanceTests.cs region=conformance
```

Six checks, all reading the `.sql` files off disk and the EF model out of memory,
none of them opening a connection, so they run on a CI runner with no Azure
credential:

- every table the model maps is declared,
- every column the model maps has the type and nullability the DDL gives it,
- every column the DDL declares is mapped, because a `NOT NULL` column nothing
  writes fails every insert on the day it is added,
- the primary keys agree,
- every foreign key the model believes in is declared,
- every index the model believes in is declared.

Physical design is checked separately, and against the DDL only, because the
model does not know about it and should not:

```live path=api/TheYard.Tests/SchemaConformanceTests.cs region=physical
```

The reader those tests use is not a T-SQL parser. It understands the shape the
files in this repository are written in and throws rather than guessing when it
meets anything else, which is affordable because the real parser runs in the
build: `dotnet build` compiles the same files with Microsoft's SQL project SDK,
so a column with a type that does not exist fails there, not here.

## The application cannot change the schema

This is the part that is a security improvement rather than a tidiness one.

The container's managed identity holds `db_datareader` and `db_datawriter`. It
does not hold `db_ddladmin`, and the grant was removed once the SQL project took
over, so the running application cannot create, alter or drop a table. It is not
a policy that it does not; it is not permitted to.

What follows from that is in `YardDatabase.BringSchemaUp`, which is the whole of
the difference between the two providers:

```live path=api/TheYard.Infrastructure/EfSources.cs region=schema
```

On SQL Server it asks whether the schema it maps to is present and refuses the
store if it is not, falling back to the file-backed catalogue that ADR: The
relational store built. On SQLite it applies its own migrations, because a SQLite
database here is created and thrown away by the process that uses it: a scratch
file per test, and a container-lifetime file in the fallback. Nothing publishes
to it and nothing else reads it, so it has no second authority to disagree with.

## How the schema gets to Azure

```
dotnet build api/TheYard.Database/TheYard.Database.sqlproj
sqlpackage /Action:Publish ^
  /SourceFile:api/TheYard.Database/bin/Debug/TheYard.Database.dacpac ^
  /TargetConnectionString:"Server=tcp:...;Authentication=Active Directory Default;"
```

Deliberately, by a person, and not by the deploy. A schema change and a code roll
are different kinds of risk: a container can be rolled back by pointing at the
previous image, and a dropped column cannot. SqlPackage compares the DACPAC to
the live database and applies the difference, so the publish is incremental and
repeatable rather than a script somebody has to remember not to run twice.

The deploy pipeline is unchanged by this. It rolls a container that maps to a
schema, and if the schema is not there the container says so on the Admin tab and
serves the catalogue from files.

## Consequences

- The database is a reviewable artifact. A schema change shows up in a pull
  request as SQL, and a reviewer who does not read C# can still say whether the
  column is right.
- The schema is portable. Every statement in `api/TheYard.Database` is standard
  enough to be read by anything that speaks T-SQL, and the parts that are not,
  the clustered index choices, are the parts a different engine would want to
  make differently anyway.
- CI compiles the database on every push, so a broken column fails the build the
  same way a broken C# file does.
- The application lost the right to change its own schema, which is the point.
- The EF migrations for SQL Server were deleted. They existed for about an hour
  and are in the history rather than the tree, because a migrations chain nobody
  applies is a second definition of the schema waiting to be believed.
- Identity's seven tables are now this repository's to maintain across upgrades.

## Files

- [`api/TheYard.Database`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Database): the schema, and the authority.
- [`api/TheYard.Database/Tables/Vehicles.sql`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Database/Tables/Vehicles.sql): the catalogue, with the reason beside every length.
- [`api/TheYard.Database/Tables/Bids.sql`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Database/Tables/Bids.sql): the concurrency token, the foreign key, and the two things deliberately absent.
- [`api/TheYard.Tests/SchemaConformanceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/SchemaConformanceTests.cs): what holds the mapping to the schema.
- [`api/TheYard.Infrastructure/EfSources.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/EfSources.cs): the two ways a schema arrives, and the refusal when it has not.
- [`docs/ADR-039-sql-server-backend.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-039-sql-server-backend.md): the database itself, and the connection string that is not a credential.
- [`docs/ADR-041-two-providers-explained.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-041-two-providers-explained.md): the same setup, walked at a new developer's level.
