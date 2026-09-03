# ADR: The SQL Server backend

Status: accepted, 2026-09-03, shipped as 1.0.0.49. Steve's ask: "make sure we
have a SQL backend that is implemented correctly in SQL Server with Entity
Framework with clear diagrams that is ready to expand."

## The connection string that is not a credential

Steve's second instruction that day was "when possible we want to avoid standard
connection strings as they are a security risk", and this is the part of the
work that answers it, so it goes first.

A standard connection string is a security risk because it carries a password.
It ends up in a settings file, in a deployment variable, in a screenshot of a
deployment variable, in a support ticket, and in the shell history of whoever
tested it. Rotating it means finding every copy. The usual answers are to make
the copies harder to read: a key vault, a secure setting, a masked log. All of
them are still guarding a password.

There is no password here. The logical server was created with
`--enable-ad-only-auth`, which means it has no SQL administrator login at all
and no ability to grow one. The container authenticates as the user-assigned
managed identity it already carried for pulling its own image,
`id-theyard-ss`, and the token comes from Azure at the moment it is needed.

Before, on the storage this replaces:

```
Data Source=/app/state/yard.db
```

After:

```
Server=tcp:sql-theyard-ss-westus3.database.windows.net,1433;Initial Catalog=sqldb-theyard-ss;
Encrypt=True;TrustServerCertificate=False;Connect Timeout=30;
Authentication=Active Directory Managed Identity;User Id=2888a6ca-be1c-46a5-a1de-c666b1d193e5;
```

Every field in that is public information. The server name is in DNS. The
database name is in the resource group. The client id is an identifier that
already appears in `Program.cs` beside the Application Insights wiring. There is
nothing in it to steal, nothing to rotate, and nothing that stops working if it
appears in a log.

Two things follow from that and both are worth saying out loud.

**The identity's database user was created without Microsoft Graph.** The usual
`CREATE USER [name] FROM EXTERNAL PROVIDER` needs the logical server to hold a
directory permission so it can look the name up. Giving a database server the
ability to read a directory is a permission that outlives the reason it was
granted. The user was created from the SID of the identity's client id instead,
which the server can verify without asking anybody:

```sql
CREATE USER [id-theyard-ss] WITH SID = 0xCAA688281CBEA546A1DEC666B1D193E5, TYPE = E;
ALTER ROLE db_datareader ADD MEMBER [id-theyard-ss];
ALTER ROLE db_datawriter ADD MEMBER [id-theyard-ss];
```

Read back from `sys.database_principals` afterwards, because a grant nobody
verified is a grant nobody has:

```
principal: name=id-theyard-ss  type_desc=EXTERNAL_USER  sid_hex=0xCAA688281CBEA546A1DEC666B1D193E5
role: db_datareader
role: db_datawriter
```

Two roles, and the third one is the interesting part. `db_ddladmin` was granted
first, because the plan at that point was for the container to apply its own
migrations at startup, and then removed when the schema became the SQL project's
(ADR: Data first, and the database in source control). The running application
now cannot create, alter or drop a table. It is not that it does not; it is not
permitted to.

**The code still refuses to print it.** `YardConnection.Describe` returns
`"Azure SQL Database"` and nothing else, and a test asserts that the server, the
database name and the user id are all absent from what it returns. There is no
secret in today's connection string, but that is a fact about today's
configuration and not a property of the method. The next person to add a setting
should not have to notice that this method would have printed it.

## Context

ADR: The relational store put the catalogue and the bids into SQLite through EF
Core, behind the ports that already existed, and was explicit about what it did
not give you: `/app/state` is the container's own writable layer, so a bid
survived a process restart and did not survive a deploy. That record named the
fix and declined to do it, because it needed a resource in a subscription this
project was not allowed to add to.

Steve authorised exactly one exception on 2026-09-03: one Azure SQL Database and
the logical server it needs.

## The database, and what it costs

Priced before it was created, from the Azure retail price API rather than from
memory, for the region the container runs in:

| meter, westus2 | measured |
| --- | --- |
| General Purpose serverless Gen5, 1 vCore | $0.521758 per vCore-hour |
| General Purpose serverless Gen5, `1 vCore - Free` | $0.00 |
| General Purpose data stored | $0.115 per GB-month |
| General Purpose data stored, free | $0.00 |
| Single Basic, 5 DTU | $0.161 per day, $4.90 per month |

Azure SQL Database has a free limit: 100,000 vCore-seconds of serverless compute,
32 GB of data and 32 GB of backup per database per month, renewing on the first,
on any subscription type, and it does not expire. Both free meters exist in this
region at $0.00, which is the check that the offer is real here rather than
announced somewhere else.

The database was created with it, and read back rather than assumed:

```
sku            GP_S_Gen5_2
minCapacity    0.5 vCore
autoPauseDelay 60 minutes
maxSize        32 GB
useFreeLimit   true
exhaustion     AutoPause
backup         Local
status         Online
location       westus3
```

Committed cost: $0.00 per month. When the monthly allowance runs out the database
auto-pauses until the first rather than billing, which is the setting
`freeLimitExhaustionBehavior: AutoPause` and is not reversible once changed to
the other option.

**It is in westus3 and the container is in westus2**, because West US 2 refused:

```
(RegionDoesNotAllowProvisioning) Location 'West US 2' is not accepting creation
of new Windows Azure SQL Database servers at this time.
```

That is the third capacity refusal this project has taken from Azure, after App
Service quota and Container Apps (ADR: Deployment strategy). The cost of the
extra region is one hop on the writes, and nothing at all on the reads, because
the catalogue is read into memory once at startup and never queried again per
request.

One thing that refusal taught, and it cost a run to learn: **a create that fails
still pins the name to the region it failed in.** The first retry loop kept one
server name and walked nine regions, and every region after the first answered
`InvalidResourceLocation: the resource already exists in location 'westus2'`,
which is ARM remembering a resource that `az sql server list` says does not
exist. The loop that worked uses a name per region.

## Decision

**Two providers, one model, and the difference is in one method.** Azure SQL
Database is what the deployed container talks to. SQLite is what a developer and
a CI runner get, because neither has an Azure credential and neither is getting
one. `YardConnection` is the only place that knows which is which: it picks the
provider, names that provider's migrations assembly, and adds the retry policy a
cloud database needs.

**The provider is chosen by whether there is a SQL Server to talk to,** and a
placeholder counts as nothing:

```live path=api/TheBlock.Infrastructure/YardConnection.cs region=choose
```

The `__` test is the difference between a bad deploy and an outage. The
container's SQL setting is substituted at roll time by the deploy exactly as the
Application Insights key is, and a substitution that fails leaves the literal
`__YARD_SQL_CONNECTION__` behind. Reading a placeholder as "no SQL Server here"
means a broken deploy falls back to the SQLite path that ran this site for a
week, instead of crash-looping against a string that is not a connection string.

**Retries, because a serverless database is sometimes asleep:**

```live path=api/TheBlock.Infrastructure/YardConnection.cs region=configure
```

The database auto-pauses after an idle hour. The first connection after that
wakes it, and while it is waking it answers with a transient error rather than a
connection. The numbers are a budget rather than a preference: this runs during
startup, before the container serves anything, and the deploy gives a new build
five minutes to answer, so four retries with a ceiling of eight seconds over a
thirty-second connect timeout is about ninety seconds of patience. That is enough
for a serverless resume, which takes thirty to sixty, and short enough that a
database which is genuinely gone falls through to the path below while the deploy
is still watching. Waiting longer would not fix an outage, it would turn it into
a failed deploy as well.

**The site still comes up when the database does not.** ADR: The relational
store built that and this did not touch it: `YardDatabase.Prepare` runs before
anything is registered, and if it throws, the composition root registers the JSON
file readers and `NullBidStore` instead. A paused serverless database is now a
routine event rather than a hypothetical one, which makes that path more
important than it was, not less. The health check names the provider so the
Admin tab says which one is serving.

## The schema, done properly

Steve scoped this: the current model done properly. Vehicles, photos, accounts,
bids. Correct types and lengths, real foreign keys, the indexes the queries
actually need, a concurrency token on bids. Nothing speculative.

Every decision below is written in `api/TheBlock.Database` as DDL and mirrored in
the EF model, and a conformance test fails the build if the two disagree. The
catalogue table, with the reason beside every length:

```live path=api/TheBlock.Database/Tables/Vehicles.sql region=*
```

The mapping that has to agree with it:

```live path=api/TheBlock.Infrastructure/YardDbContext.cs region=model
```

**Lengths on every text column this application owns.** Without them every string
is `nvarchar(max)` on SQL Server, which cannot be indexed, is stored off-row past
8,000 bytes, and tells a reader nothing about what belongs in it. The lengths
were chosen from what the dataset actually holds, with headroom, and the longest
observed value in the 200-record seed is in brackets: id 64 (36), make 64 (10),
model 64 (14), trim 64 (19), body style 32 (9), colours 32 (16), engine 128 (25),
transmission 64 (12), drivetrain 16 (3), fuel type 32 (8), condition report 1024
(143), title status 32 (7), province 64 (16), city 64 (11), dealership 128 (24),
lot 32 (6).

The id gets 64 rather than 40 because bids reference the synthetic ids, which are
the seed id plus six characters, and a foreign key column and the column it
points at have to agree.

**A VIN is `varchar(17)`** and it is the only non-Unicode column here. Seventeen
characters from a defined alphabet is not a guess about this dataset, it is ISO
3779. It is deliberately not `char(17)`: fixed-length columns pad on read, and a
padded VIN stops equalling the one in the record it came from.

**A condition grade is `decimal(3,1)`, not a float.** It is a number between 1.0
and 5.0 to one decimal place that gets compared and displayed, never accumulated,
so the exact type is the right one and `float`'s approximation of 2.7 is not. The
CLR property stays `double`, because that is what the domain record uses and the
persistence layer does not get to change the shape of what it stores. A converter
sits between them.

**An auction start is `datetime2(0)`.** The dataset carries it as
`2026-04-05T19:00:00`, a local wall-clock instant to the second with no zone,
which is exactly `datetime2(0)`. It is sortable and comparable in the database
now rather than only in C#. The property stays a string for the same reason the
grade stays a double, and the converter is asserted to round-trip every one of
the 200 rows in the dataset rather than an example:

```live path=api/TheBlock.Infrastructure/YardDbContext.cs region=auction-start
```

**Damage notes and images stay JSON columns.** They are read and written whole
and never queried into, which is the case a JSON column is for. The day something
filters on a damage note is the day this becomes a table.

**The catalogue is clustered on the column it is read in order of.** A clustered
index is the table's physical order, and SQL Server puts it on the primary key by
default. The only query this table ever serves is `SELECT ... ORDER BY Seq`, run
once at startup, so `Seq` is the clustered index here and the primary key on `Id`
is explicitly not. This one is decided in the DDL and nowhere else, because
physical design belongs beside the storage and the EF model has no business
knowing about it:

```sql
CONSTRAINT [PK_Vehicles] PRIMARY KEY NONCLUSTERED ([Id])
CREATE UNIQUE CLUSTERED INDEX [IX_Vehicles_Seq] ON [Vehicles] ([Seq]);
```

**One real foreign key, and one that would be a lie.**

A bid now references its account, with a cascade, so deleting an account takes
its bids with it and the database is what guarantees that rather than something
the application has to remember. Before this, a bid whose account had been
deleted stayed in the table forever, was loaded into `BidService` at every
startup, and counted toward a vehicle's standing price on behalf of nobody.

There is deliberately no foreign key from a bid to the catalogue. The `Vehicles`
table holds 200 rows, which `SyntheticVehicleSource` expands in memory to 100,000
by deriving ids from them, and a visitor bids on the expanded set. A constraint
there would reject 99.8 per cent of legitimate bids. It becomes correct the day
the expansion is persisted, and a test asserts its absence so that the day it is
added, something fails and sends the next person to this paragraph.

**One index removed.** `Bids` had an index on `UserId` and its primary key is
`(UserId, VehicleId)`. The leading column of the key already answers every
question that index existed for, so it was a second copy of the first half of the
key: a write on every bid, and nothing earned.

**A concurrency token on bids.** Two containers, or two requests that get past
one container's lock, can both read a bid row and both decide to write it. The
token makes the second write fail instead of silently overwriting the first,
which is a lost update, and a lost update on an auction is somebody's money.

On SQL Server it is a real `rowversion`: 8 bytes, maintained by the database,
impossible for the application to forget to move, and declared in
`Tables/Bids.sql`. SQLite has no such type, so the store assigns one on every
save and the guarantee is the same with a different owner. The store retries a conflict rather than failing the bid, because the
correct answer to "somebody moved this row" is to start again from what is there
now:

```live path=api/TheBlock.Infrastructure/EfSources.cs region=bid-store
```

**Identity's own tables were left alone**, including the `nvarchar(max)` columns
it uses for password hashes and stamps. Those belong to a framework whose hashing
algorithm can change under this application, they hold a handful of rows, and
bounding them would trade nothing measurable for a truncation that would only
appear on the day somebody upgraded. The test that requires a length on every
text column names the three tables this application owns, and says why.

## The schema does not come from here

The first version of this work generated the SQL Server schema from the EF model
and applied it with migrations at startup. That lasted about an hour, until Steve
asked for data first and gave the reason: the data structure outlives the
framework that reads it.

So the schema is `api/TheBlock.Database`, a SQL project of hand-written DDL that
builds to a DACPAC, and this application maps to it. The decision, what it costs
and what enforces it are in ADR: Data first, and the database in source control.
What matters here is the shape it leaves behind:

```
api/TheBlock.Database/            the SQL Server schema, the authority, published by SqlPackage
api/TheBlock.Migrations.Sqlite/   the SQLite schema's history, applied by the process that uses it
```

The SQLite history is the same history it has always had, moved out of
`TheBlock.Infrastructure` and renamed, carrying the same migration ids, so a
database created last week still matches its own `__EFMigrationsHistory` rows.
The SQL Server migrations were deleted rather than kept, because a migrations
chain nobody applies is a second definition of the schema waiting to be believed.

One design-time factory still exists, in the host project, because SQLite still
has migrations to write:

```live path=api/TheBlock.Api/DesignTime.cs region=design-time
```

### What the first publish said, and what it cost to ignore

Publishing the schema warned four times:

```
Warning! The maximum key length for a clustered index is 900 bytes.
The index 'PK_AspNetUserTokens' has maximum length of 2700 bytes.
The index 'PK_AspNetUserLogins' has maximum length of 1800 bytes.
The index 'PK_AspNetUserRoles' has maximum length of 1800 bytes.
The index 'PK_Bids' has maximum length of 1028 bytes.
```

That is ASP.NET Core Identity's default key width, `nvarchar(450)`, which is 900
bytes on its own, so any composite key built from two of them is over SQL
Server's limit before this application adds anything. It is a real defect and not
a style note: an insert of a long enough value fails.

Nothing in the C# would have said so. The model compiles, the tests pass, and the
number only appears when a real SQL Server is asked to build the thing. That is
an argument for the SQL project rather than against it: the schema got compiled
by the engine that has to hold it, and the engine objected.

Identity's key columns are now `nvarchar(128)` here, which makes the widest
composite key 768 bytes. The ids this application creates are GUIDs, so 128 is
generous, and the widths are declared in the DDL and asserted against the model
by the conformance test.

The fix then failed once, quietly, and the way it failed is worth more than the
fix. The DDL was changed, the solution was built, the tests passed, the schema
was published, and the database came back with the same four warnings and the
same 450-byte columns. `dotnet build` on the solution does not build a SQL
project. It reported success, the DACPAC on disk was the previous one, and the
publish shipped a schema that did not contain the change.

Nothing in that sequence looked wrong. The lesson is the one this repository
keeps relearning: the gate is an independent read of the thing itself, not a
tool's report about it. The database's own `sys.indexes` is what said so, and CI
now has a `database` job that builds the SQL project explicitly and fails if no
package comes out of it.

## Tests that run where there is no Azure

The suite runs in CI, CI has no Azure credential, and it is not getting one. So
the SQL Server schema is asserted from the model rather than against a server:
each test builds the context with the SQL Server provider, reads the model or
generates the CREATE script, and never opens a connection.

```live path=api/TheBlock.Tests/SqlServerModelTests.cs region=lengths
```

That covers the lengths, the types, the keys, the clustering, the token and the
absent foreign key. What it cannot cover is whether Azure accepts the DDL, which
is proved once, by the container, when it migrates on its first boot, and shows
up on the Admin tab if it fails.

The constraints that both providers share are exercised against a real engine,
which is SQLite for the same reason it is SQLite in CI:

```live path=api/TheBlock.Tests/RelationalConstraintTests.cs region=concurrency
```

## What this does not give you

**A second replica.** One container still writes, and the in-process lock in
`BidService` is still what orders bids. The concurrency token is what would make
a second writer safe rather than what makes it exist.

**A private network path.** The logical server is reachable from Azure services
through the `AllowAzureServices` firewall rule, which is the 0.0.0.0 rule and not
an open-internet rule, and the runner's own address is a second rule naming one
address. The container group has a public egress address that changes on every
roll, so pinning it would break the next deploy. What actually gates access is
that the server accepts only Entra tokens, and the only principal with a database
user is the container's identity. A private endpoint is the real answer and it is
a new resource this project is not authorised to add.

**Durable backups anyone has restored.** Backup storage is Local redundancy
inside the free limit. Nobody has tested a restore.

## The review of this diff

Written after the work, before the ship, by reading the whole diff again as
somebody who did not write it. Nine things came out of it and all nine are in the
tree.

**The health check said too much.** `Describe()` returned
`"SQL Server, tcp:sql-theyard-ss-westus3.database.windows.net,1433"`, which put a
server name in a log and on a public endpoint. It returns `"Azure SQL Database"`
now, and a test asserts that the server, the database name and the user id are
all absent from what it returns. There is no secret in today's connection string,
but that is a fact about today's configuration and not a property of the method.

**The retry policy had no budget.** Six retries with a twelve-second ceiling over
a sixty-second connect timeout can spend minutes before giving up, and it spends
them during startup, inside the five minutes the deploy gives a new build to
answer. It is four retries, an eight-second ceiling and a thirty-second connect
timeout now, which is about ninety seconds: enough for a serverless resume, short
enough that a database which is genuinely gone falls through to the file-backed
path while the deploy is still watching.

**Two lists of the same four table names**, one in an array and one inside the
SQL, on a check whose entire job is to notice drift. One list now.

**An index that was a second copy of half a key.** `Bids` had an index on
`UserId` and a primary key of `(UserId, VehicleId)`. Removed, with the reason in
the DDL where the next person will look.

**Identity's key width**, which the first publish caught and is described above.

**A test helper living inside a test class file.** `Repo` is used by three
classes and now has its own.

**A gate that read UTF-8 as Windows-1252.** The em dash check reported a hit in
`docs/PROJECTS.md`, which has no em dash: 0x97 is the second byte of a UTF-8
multiplication sign and an em dash in that code page. Read as UTF-8 the tree
scans clean at 234 files. Worth recording because a gate that reports a false
finding is worse than no gate: it teaches people to ignore it.

**A build that succeeded without building.** `dotnet build` on the solution does
not build a SQL project, so a schema change was published that the package did
not contain, and every tool involved reported success. CI has a job that builds
the SQL project on its own and fails if no package comes out.

**A browser suite that looked broken and was not.** Four full runs, one or two
failures each, a different test every time, all passing in isolation. The
temptation was to call it a flake. Instead the API was started deliberately, kept
alive after the suite and asked what it had recorded: `/api/vehicles?limit=100`
in 328 to 397 ms, `/api/errors` holding nothing but the test's own probe, every
health check green, and 43 of 43 passing in 1.6 minutes. The cause was on the
first line of that same log: eight orphaned `dotnet` processes left by a day of
test runs, and a Playwright config that reuses whatever server it finds
listening. The failures were a loaded machine, and that sentence is now a
measurement rather than a hope.

## Addendum, the same day: the release that shipped on SQLite

1.0.0.49 went out with every gate green and came up on SQLite. The site was
healthy, `/readyz` said ready, 42,775 auctions were answering, and the health
check said, correctly, "the seed catalogue is in the store (SQLite)".

The container's own first log line settles what happened and what did not:

```
Database ready: SQLite, migrated in 2477 ms and seeded in 1158 ms,
inserting 200 vehicles and 50 photos, now holding 200 and 50
```

No error. Nothing failed to connect. `YardConnection.Choose` was handed the
literal `__YARD_SQL_CONNECTION__` and read it as "no SQL Server here", which is
exactly what that check exists for.

The placeholder survived because the deploy asked Azure for the server's fully
qualified name and the deploy's own identity is not allowed to read it. Its role
assignments are Container Instances Contributor, AcrPush and Managed Identity
Operator, and no reader role anywhere, so `az sql server show` failed, `|| true`
swallowed it, and the substitution had nothing to substitute.

The fix is to stop asking. An Azure SQL logical server's address is its name plus
the service suffix; it is not a secret, it does not change, and it does not need
a permission. The deploy composes it now.

Two things worth keeping out of this.

**The fallback is not theoretical any more.** It was written for a paused
serverless database and the first thing it actually caught was a deploy
misconfiguration. A visitor saw a working site throughout, and nothing about the
release was wrong except which store it was reading from.

**Silence was the real defect.** A roll landing on SQLite must not fail a deploy,
because that is the fallback doing its job. It must also not be invisible, and it
was: nothing said so until the health endpoint was read by hand. The Verify step
now reads `/api/health` and warns when the container is not on Azure SQL Database.
A warning, not a failure, because the difference between those two is the
difference between a safety net and a tripwire.

## Consequences

- A bid survives a deploy, which is the sentence this record exists for. It
  survived a process restart before; the storage it was written to did not
  survive the container being replaced, and now it is not in the container.
- The site has a network dependency it did not have. A paused database is a
  routine event on this tier, so the file-backed fallback moved from a safety net
  to something that runs.
- Local development and CI are unchanged. No connection string, no credential,
  no Azure. `dotnet test` behaves exactly as it did.
- Adding a column is now four edits rather than three: the record, the row, the
  mapping, and a length on the column. The fourth is enforced by a test.
- Two migrations projects exist and both have to be kept. A schema change is two
  `dotnet ef migrations add` commands, which is written down in the junior record
  beside this one.
- The connection string is configuration, so reverting this is removing an
  environment variable rather than a deploy. `aci-export-v11.yaml` is untouched.

## Files

- [`api/TheBlock.Infrastructure/YardConnection.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Infrastructure/YardConnection.cs): the provider choice, the retry policy, and what may be said about the database.
- [`api/TheBlock.Infrastructure/YardDbContext.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Infrastructure/YardDbContext.cs): the model, both providers, and the value converters.
- [`api/TheBlock.Infrastructure/Rows.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Infrastructure/Rows.cs): the row types, and the concurrency token.
- [`api/TheBlock.Infrastructure/EfSources.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Infrastructure/EfSources.cs): the adapters, the seed, and the store that retries a conflict.
- [`api/TheBlock.Api/DesignTime.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/DesignTime.cs): the one design-time factory, and the two commands that use it.
- [`api/TheBlock.Database`](https://github.com/SteveStout/TheYard/tree/main/api/TheBlock.Database): the SQL Server schema, hand written, and the authority for it.
- [`api/TheBlock.Migrations.Sqlite`](https://github.com/SteveStout/TheYard/tree/main/api/TheBlock.Migrations.Sqlite): the SQLite schema's history.
- [`api/TheBlock.Tests/SchemaConformanceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/SchemaConformanceTests.cs): what holds the mapping to the schema.
- [`api/TheBlock.Tests/SqlServerModelTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/SqlServerModelTests.cs): the schema, asserted without a SQL Server.
- [`api/TheBlock.Tests/RelationalConstraintTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/RelationalConstraintTests.cs): the foreign key and the token, exercised against an engine.
- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard.yaml): the setting, and the placeholder that is not a connection string.
- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml): where the setting is built, at roll time, from the resource itself.
- [`docs/ADR-040-database-source-control.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-040-database-source-control.md): the SQL project, why it is the authority, and what enforces that.
- [`docs/ADR-041-two-providers-explained.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-041-two-providers-explained.md): the same setup, walked at a new developer's level.
