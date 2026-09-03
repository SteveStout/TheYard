# ADR: Entity Framework, explained

Status: accepted, 2026-09-03, shipped as 1.0.0.43. A companion to
ADR: The relational store, written for a developer meeting EF Core for the
first time. That record says what was decided and why; this one walks the
setup file by file and explains what each piece is actually doing.

It exists for the same reason ADR: Program.cs, explained does. Generated
configuration is the easiest place in a project to carry things nobody can
account for, and a data layer is the worst place for that to be true.

## The five pieces

**A `DbContext` is a session with the database.** `YardDbContext` is a class
that names the tables it can see and, through `DbSet<T>`, gives you something
you can write LINQ against. When you write `db.Vehicles.Where(...)`, EF turns
that expression into SQL and runs it. The context also tracks the objects it
handed you so that changing one and calling `SaveChanges()` produces an UPDATE
for exactly the columns you touched.

A context is not thread-safe and is not meant to be long-lived. It is a unit of
work: open, do a thing, dispose.

**`IDbContextFactory<T>` is how you get one when you are not in a request.**
The usual advice for a web application is `AddDbContext`, which gives every
HTTP request its own context and disposes it at the end. That is the right
default and it is not what this application needs. The catalogue is read once
at startup, before any request exists, and the two sources and the bid store
are singletons that outlive every request. `AddDbContextFactory` registers a
factory instead, and each of them opens a context for the length of one
operation and disposes it. `using var db = factory.CreateDbContext();` is that
in one line.

**A migration is the schema, as code, with a history.** Running
`dotnet ef migrations add InitialCreate` compares the model in `YardDbContext`
against the last known state and writes three files: the migration itself, with
an `Up` that creates the tables and a `Down` that drops them; a `.Designer.cs`
holding a snapshot of the model at that point; and an updated
`YardDbContextModelSnapshot.cs`, which is what the next migration will compare
against.

At startup, `db.Database.Migrate()` looks at a table called
`__EFMigrationsHistory`, sees which migrations have already been applied to
this particular database, and runs the ones that have not. That is the whole
mechanism, and it is why the alternative was rejected: `EnsureCreated()` makes
the tables and writes no history, so the second time the model changes there is
nothing to compare against and no way forward except deleting the database.

**The design-time factory keeps the tooling out of the application.** When
`dotnet ef` needs to know what the model looks like, it will by default start
your application to find a context in its service provider. That would be fine
in most projects and is actively harmful in this one: this application migrates
and seeds during startup, so generating the very first migration would boot an
app that tries to seed tables the migration has not created yet.
`YardDbContextFactory` implements `IDesignTimeDbContextFactory<YardDbContext>`,
which the tooling prefers over booting anything. It hands back a context with a
throwaway connection string, because the tooling only ever reads the model from
it.

There is a matching detail worth knowing because the error message is the only
place it is stated clearly: `Microsoft.EntityFrameworkCore.Design` has to be
referenced by the **startup** project, not only by the project holding the
context. That is what `dotnet ef migrations add` failed on the first time it
was run here.

**Primitive collections are stored as JSON.** `VehicleRow.DamageNotes` is a
`List<string>`, and EF Core maps it to a single text column holding a JSON
array. No join table, no second entity. That is the right shape for a list that
is only ever read back whole with its owner, which these two are.

## The three files with the same name

Look in the directory holding the database during a run and there are three
files, not one:

| file | what it is |
| --- | --- |
| `yard.db` | the database |
| `yard.db-wal` | the write-ahead log |
| `yard.db-shm` | shared memory, coordinating readers and the log |

SQLite is in write-ahead logging mode. A committed transaction is written to
the log and fsynced there, and moved into the database proper by a checkpoint
later, usually at a clean shutdown. That is why a database file can be 4,096
bytes, exactly one page, while holding two hundred vehicles: they are in the
log, and the next process to open the pair replays it.

Two practical consequences. Copying `yard.db` on its own can copy an empty
database, so the log has to come with it. And a size check on the database file
alone can report that the data vanished when it did not, which is exactly what
happened here and is written up in ADR: The relational store.

## Why the row types are not the domain records

`TheBlock.Data.Vehicle` is a sealed record with `required init` properties and
`IReadOnlyList<string>` collections. Every one of those choices is deliberate
and every one of them is awkward for a persistence layer: init-only members
cannot be set field by field after construction, and a read-only interface is
not something EF can populate.

The alternative to a second type is loosening the first one, and that trade
runs the wrong way. The domain record is used everywhere and its immutability
is load-bearing: `with` copies drive the bid overlay, and value equality drives
the tests. So `VehicleRow` is a plain mutable class, `VehicleRows` maps between
them in one file, and the awkwardness is confined to a mapper instead of spread
through the application.

The honest cost is in the other record: adding a field to a vehicle is now
three edits rather than one.

## What `AsNoTracking` is doing there

By default a context remembers every entity it returns so it can work out what
changed later. That bookkeeping is worth paying for when you intend to write.
The catalogue reads never write, so `AsNoTracking()` tells EF to skip it. On a
few hundred rows the difference is small. It is in the code because the habit
matters more than this instance: a read path that tracks is a read path that
allocates and holds objects for no reason.

## What the seed is checking, and why

`YardSeed.EnsureSeeded` asks whether the table is empty, not whether the file
is new. Those come apart in exactly one case that matters: a process that died
partway through its first seed leaves a database with a schema and no rows. The
emptiness check fills it on the next start. A "have I seeded before" flag would
have recorded the attempt and left the tables empty forever.

## In the code

The context and its model (`api/TheBlock.Infrastructure/YardDbContext.cs`):

```live path=api/TheBlock.Infrastructure/YardDbContext.cs region=model
```

The seed (`api/TheBlock.Infrastructure/EfSources.cs`):

```live path=api/TheBlock.Infrastructure/EfSources.cs region=seed
```

The bid store, which is the only thing here that writes
(`api/TheBlock.Infrastructure/EfSources.cs`):

```live path=api/TheBlock.Infrastructure/EfSources.cs region=bid-store
```

Startup: migrate, then seed, then serve (`api/TheBlock.Api/Program.cs`):

```live path=api/TheBlock.Api/Program.cs region=migrate-and-seed
```

## If you want to change the schema

1. Change the row class in `Rows.cs`, and the mapping in `EfSources.cs` if the
   field also exists on the domain record.
2. `dotnet ef migrations add <Name> --project api/TheBlock.Infrastructure
   --startup-project api/TheBlock.Api`
3. Read the generated `Up` method. It is ordinary C# and it is the only place
   the change becomes real.
4. Commit all three generated files. The snapshot is not optional; without it
   the next migration compares against the wrong model.

There is no step for applying it. The application does that on start, and a
container rolling onto a database from an older build brings it forward on the
way up.

## Files

- [`api/TheBlock.Infrastructure/YardDbContext.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Infrastructure/YardDbContext.cs)
- [`api/TheBlock.Infrastructure/Rows.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Infrastructure/Rows.cs)
- [`api/TheBlock.Infrastructure/EfSources.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Infrastructure/EfSources.cs)
- [`api/TheBlock.Infrastructure/Migrations`](https://github.com/SteveStout/TheYard/tree/main/api/TheBlock.Infrastructure/Migrations)
- [`docs/ADR-033-relational-store.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-033-relational-store.md): what was decided, and what this deliberately does not give you.
- [`docs/ADR-018-program-cs-explained.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-018-program-cs-explained.md): the same treatment for the composition root.
