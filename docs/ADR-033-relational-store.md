# ADR: The relational store

**Read this first: the store described below is no longer the deployed one.**
This record is the decision to have a relational store at all, and it is still
the reason the ports, the row types, the seeding and the fallback are shaped the
way they are. What changed later the same day is the engine underneath it and who
owns the schema: the deployed site is on Azure SQL Database, and the schema is a
SQL project rather than something Entity Framework creates. That is
ADR: The SQL Server backend and ADR: Data first, and the database in source
control.

Everything below is left as it was written rather than edited into agreement with
today. A record that quietly becomes true again is worse than one that says when
it stopped being true, and the measurements in here are measurements of SQLite
and should keep saying so. Where a sentence is in the present tense and no longer
is, read it as "as of 1.0.0.43".

The one paragraph worth carrying forward without a caveat is the fallback. A
container that cannot reach its store serves the catalogue from files and says so
on the health check. That was built here, it is unchanged, and on 2026-09-03 it
was the only reason a release that shipped with a misconfigured connection string
was invisible to everyone using the site.

---

Status: accepted, 2026-09-03, shipped as 1.0.0.43. Steve's ask: "swap the
in-memory or file-backed data layer for a real relational store behind the
existing ports, with migrations, seeding, and a bid that survives a restart."

## Context

Two things were being kept in places that could not keep them.

The catalogue was `data/vehicles.json`, read once at startup by
`JsonFileVehicleSource` and expanded to a hundred thousand records by a
decorator. That is a perfectly good arrangement for a fixed dataset and it was
never the problem.

The bids were the problem. `BidService` held them in a `ConcurrentDictionary`
and its own comment admitted what that meant: "held in API memory (this is an
isolated demo; a real system would persist per-user bids)". Every container
roll erased every bid on the site. A deploy is a roll, so an auction site that
shipped twice a day forgot its auction twice a day.

## Decision

**SQLite through EF Core, in the Infrastructure layer, behind the ports that
already existed.** `IVehicleSource` and `IPhotoManifestSource` are unchanged.
`EfVehicleSource` implements the first one, the synthetic scale-up still
decorates it, and nothing in Application or Domain learned that a database
exists. The one seam that is new is `IBidStore`, which is a port for the same
reason the other two are: `BidService` decides the rules and does not get to
know where the answer is kept.

**Row types, not domain records.** `TheYard.Data.Vehicle` is a sealed record
with init-only members and `IReadOnlyList<string>` collections. Those are the
right choices for a value the whole application passes around and the wrong
ones for something a mapper builds a field at a time, so `VehicleRow` is its
own class and `VehicleRows` holds the mapping in one place. The cost is a dull
file that has to be kept complete. The benefit is that the domain record never
has to become whatever EF finds convenient.

**A `Seq` column, because a table has no order.** The synthetic scale-up
expands the seed catalogue deterministically from its order, so a set that came
back in a different order would be a different hundred thousand vehicles and a
different set of test expectations. The column records where each row sat in
the seed file and every read is ordered by it.

**Natural keys and no indexes.** A vehicle already has an id, a photo already
has a unique file name, and a bid is one per vehicle by definition. There are
no indexes beyond the keys and the uniqueness of `Seq`, because there are no
queries: both catalogue tables are read whole, once, at startup, and every
filter and sort after that runs in memory. An index here would cost writes at
seed time and earn nothing.

**Read once, write through, store first.** The catalogue is read at startup
into `InventoryService`'s `Lazy`, and the bids are read at startup into
`BidService`'s dictionary. Bidding writes to both the dictionary and the store,
inside the lock that was already there, and the order is the store and then the
dictionary. The other order shipped first and is wrong in a way that looks
harmless: a store that throws would leave the dictionary holding a bid the
caller had just been told had failed, displayed as winning until the next
restart deleted it. Writing the store first means a failed write is a bid that
did not happen anywhere, which is the answer the caller already has. This is the only shape that works: the
bid overlay runs over a hundred thousand vehicles on a listing request, so a
per-row query would not be a slower feature, it would be no feature.

**Migrations, not `EnsureCreated`.** The schema's history is a set of files in
this repository, applied by `Database.Migrate()` at startup, so a container
starting against an older database brings it forward instead of finding a shape
it half recognises. A test asserts that `GetAppliedMigrations()` is not empty,
which is the difference the two approaches leave behind.

**Seeded from the files that used to be the catalogue.** First boot fills the
tables from `JsonFileVehicleSource` and `JsonFilePhotoManifestSource`. That
keeps `npm run data` the way the dataset is regenerated and means a fresh
database cannot drift from the file it came from. The check is whether the
table is empty, not whether the file is new, so a process that died halfway
through seeding is not left half seeded forever.

**The connection string is configuration, and its absence is a scratch file.**
`ConnectionStrings:Yard` names the database. Without it the process gets a
uniquely named file in the temp directory, logs a warning saying so, and
deletes it on shutdown. That is what every test wants, and it is a better
answer for a misconfigured deploy than quietly writing somewhere nobody thinks
to look. The container sets it to `/app/state/yard.db`, in a directory the
Dockerfile creates and chowns because `/app` itself belongs to root.

## The numbers

Measured on the development machine, each side built before it was measured,
the request timings being the median of four runs against a warm process.

| | before, JSON files | after, SQLite |
| --- | --- | --- |
| cold start to `/healthz`, empty database | 3,137 ms | 4,539 ms |
| cold start, database already seeded | | 3,984 ms |
| of which migrating | | 635 ms |
| of which seeding 200 vehicles and 50 photos | | 439 ms |
| a 100-vehicle listing | 264 ms | 265 ms |
| a text search over 100,000 records | 265 ms | 256 ms |
| API suite, test execution | 8 s | 17 s |
| API suite, wall clock | 17 s | 24 s |

Three things to read out of that.

**The request path did not move**, which is the whole point of reading the
catalogue once at startup. The database is not on the path a visitor takes.

**A cold container costs about 1.4 seconds more.** Roughly a second of it is
creating the schema and half a second is inserting 250 rows. Every container
roll pays it, because nothing is mounted and the database starts empty each
time.

**The API suite roughly doubled**, from eight seconds of execution to
seventeen, because every test class now boots an application that migrates and
seeds its own scratch database. That is a real cost and it is the one thing
here worth revisiting: seeding once into a template file and copying it per
class would recover most of it. It has not been done, because eight seconds of
CI time is not yet worth the machinery.

One measurement went wrong on the way, and the wrong explanation for it turned
out to be worth as much as the right one.

A shell probe reported the database file as 4,096 bytes, one SQLite page,
immediately after a boot that had demonstrably served a hundred thousand
vehicles out of it. That reads as "the data never reached disk", which would
make the whole feature theatre.

The first explanation was stale directory metadata, which is a real Windows
behaviour and was wrong here: reading the length through an open `FileStream`
gave the same 4,096. The file's own header settles it. SQLite stores its page
size at byte 16 and its page count at byte 28, and the header said one page of
four kilobytes, so the database really was one page.

The answer had been sitting next to it the whole time, and a directory listing
shows it:

| file | bytes |
| --- | --- |
| `yard.db` | 4,096 |
| `yard.db-wal` | 313,152 |
| `yard.db-shm` | 32,768 |

SQLite is running in write-ahead logging mode. Committed rows go to the log
first and are checkpointed into the database proper later. A clean shutdown
checkpoints and the main file grows; a process killed from outside does not,
and leaves a one-page database beside a three-hundred-kilobyte log. Nothing is
lost either way, because the next open replays the log, which is why the
restart test passes and always did. The test that asks about the file on disk
now counts both, since between them they are the data.

The record kept the wrong explanation for about ten minutes. It is written down
here rather than quietly replaced because "the first explanation that fits" is
the most expensive habit in debugging, and this is a cheap example of it.

## What happens when the store will not open

The container runtime on the development machine was not running the night this
shipped, so the image could not be built and run before CI built it. The
failure being risked was specific: a container that cannot create
`/app/state/yard.db` throws during startup, exits, gets restarted, and
crash-loops. The site would be down until somebody rolled it back.

The answer was not a better guess about file permissions. `YardDatabase.Prepare`
runs before anything is registered, and if it throws, the composition root
registers the JSON file readers and `NullBidStore` instead. Those were the
production path until this record existed, so the inventory, the filters, the
photos and the bidding all keep working; the only thing lost is that bids stop
outliving the process, which is exactly where this site was an hour earlier.

The failure is loud rather than silent. The exception's type and message go to
the log as an error, and the health check on the Admin tab turns red saying the
catalogue is being served from files.

The health check does not repeat the exception's message, and the first version
did. `/api/health` is public on purpose, and a storage failure's message is
usually a filesystem path: handing that to anonymous callers is the same "map
of the inside of the process" that `ProblemHandler.ServerDetail` exists to
refuse. The reason belongs in the log, which is where the trace id sends you
anyway.

The catch is deliberately over `Exception`, which is usually a smell. It is not
one here: the whole purpose of the block is that the caller keeps serving
whatever went wrong, and a narrower catch would be a list of the failures
somebody thought of.

## What this does not give you

**Durability across a container roll.** `/app/state` is the container's own
writable layer, and nothing is mounted there. A bid survives a process restart,
which is what the test proves and what the record claims. It does not survive
the container group being replaced, which is what a deploy does. Fixing that
means an Azure Files share mounted at `/app/state`, which is a new resource in
a subscription this project is deliberately not allowed to add to, so it is
written down here rather than done quietly.

That is the honest shape of it: the persistence layer is real, the storage it
writes to is not durable, and the two are separate problems with separate
costs.

**Concurrent writers.** One container, one SQLite file, one lock around
bidding. A second replica would need the file to be somewhere both could reach
and SQLite is the wrong answer at that point. This is the version that matches
the deployment, and the deployment is one container by choice (ADR: Deployment
strategy).

## In the code

Bringing the store up, or reporting that it could not be brought up
(`api/TheYard.Infrastructure/EfSources.cs`):

```live path=api/TheYard.Infrastructure/EfSources.cs region=prepare
```

The model, and why the ordering column exists
(`api/TheYard.Infrastructure/YardDbContext.cs`):

```live path=api/TheYard.Infrastructure/YardDbContext.cs region=model
```

The seam, unchanged above it (`api/TheYard.Infrastructure/EfSources.cs`):

```live path=api/TheYard.Infrastructure/EfSources.cs region=ef-sources
```

Bids, written through the lock that was already there
(`api/TheYard.Application/BidService.cs`):

```live path=api/TheYard.Application/BidService.cs region=store
```

The proof (`api/TheYard.Tests/PersistenceTests.cs`):

```live path=api/TheYard.Tests/PersistenceTests.cs region=restart
```

## Consequences

- A bid survives a restart, which is the sentence the record exists for.
- Every test class now boots an application that migrates and seeds its own
  scratch database. The cost is in the table above and it is real.
- `BidService` still works with no store at all, through `NullBidStore`, so the
  unit tests that construct it directly did not change.
- The health endpoint has a fourth check that reads the catalogue tables, so a
  database that failed to seed shows up on the Admin tab rather than as an
  empty inventory.
- The site cannot be taken down by the storage. It can be degraded by it, and
  it says so when it is.
- A scratch database runs with connection pooling off. The alternative is
  `SqliteConnection.ClearAllPools()`, which is process wide, and xUnit runs
  test classes in parallel in one process: one class tidying up its own file
  on shutdown was pulling connections out from under nine others. The
  persistence test that proved it passes alone, passes with its own class, and
  failed only in the full suite, which is the signature of a global.
- A container killed rather than stopped leaves an uncheckpointed write-ahead
  log beside the database. That is safe, because the next open replays it, and
  it is worth knowing before somebody copies a `.db` file somewhere and wonders
  why it is empty.
- Adding a field to `Vehicle` is now three edits instead of one: the record,
  the row, and the mapping. That is the price of not letting the persistence
  layer dictate the domain shape, and it is paid once per field.

## Addendum, 2026-09-03: the fallback worked and the deploy failed anyway

This record's central promise is that the site comes up when the database does
not. 1.0.0.51 was the first roll where that promise was actually tested, and it
held: the container could not reach Azure SQL Database, fell back to the JSON
files, and served the full catalogue, the filters, the photos and the bidding to
100,000 vehicles.

The deploy failed.

`/readyz` answered "ready" only when every health check passed, and the database
check had joined that set when the relational store did. So a container doing
precisely what this ADR designed it to do reported itself unfit for traffic, and
the deploy's `curl -fsS "$ORIGIN/readyz"` failed the roll.

The defect is a category error, and it is worth naming because the three probes
already had names that should have prevented it:

- **Liveness** (`/healthz`): is this process running. Restart it if not.
- **Readiness** (`/readyz`): send traffic here. Take it out of rotation if not.
- **Health** (`/api/health`): the full picture, for a person.

"The database is unreachable" is a health fact. It is not a readiness fact,
because the container can serve. Putting it in the readiness set means an outage
in a dependency takes down a service that was specifically built to survive that
outage, which is worse than having no fallback at all: with no fallback the
failure at least happens once, rather than being engineered around and then
undone by the check.

Readiness now asks only the checks that gate it:

```csharp
app.MapGet("/readyz", () =>
    RunChecks().Where(check => check.GatesReadiness).All(check => check.Status == "pass")
        ? Results.Text("ready")
        : Results.StatusCode(503));
```

and the database check is the one that declares itself out:

```csharp
Check("database", ..., gatesReadiness: false)
```

`/api/health` is unchanged: it still reports `degraded`, still names the store,
and the Admin tab still shows the failure. The deploy still warns when a roll
lands on files rather than the database. What changed is that a site which is
serving is now allowed to say so.

The test asserts both halves, because asserting only the first would pass if
every check stopped gating readiness.

## Files

- [`api/TheYard.Infrastructure/YardDbContext.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/YardDbContext.cs): the context, the model, and the design-time factory.
- [`api/TheYard.Infrastructure/Rows.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/Rows.cs): the three row types.
- [`api/TheYard.Infrastructure/EfSources.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/EfSources.cs): the adapters, the mapping, and the seed.
- [`api/TheYard.Infrastructure/Migrations`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Infrastructure/Migrations): the schema's history.
- [`api/TheYard.Application/Ports.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Ports.cs): the third port, and the null store.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the registration, the migrate and seed block, and the health check.
- [`api/TheYard.Tests/PersistenceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/PersistenceTests.cs): the restart, the seeding, the migration history, and the file on disk.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the writable directory and the connection string.
- [`docs/ADR-034-entity-framework-explained.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-034-entity-framework-explained.md): the same setup, walked at a new developer's level.
