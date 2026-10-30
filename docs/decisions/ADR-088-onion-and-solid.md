# ADR: Onion and SOLID, how this codebase holds them

Status: accepted, 2026-10-03.

## In plain words

The server is built in rings, like an onion. The rules of the auction sit in the middle, the use cases wrap them, and the database code and the web endpoints sit on the outside. Code may only use the rings inside it, never the ones outside it. That rule is checked by tests that read the compiled code, so breaking it fails the gate.

So a developer can change the database, the web framework or the hosting without touching the auction's rules, and the organization gets rules that are tested in milliseconds with no database, no web server and a fixed clock.

## The rings

[![TheYard's rings: Data at the centre, then Domain, then Application, with the host and the adapters outside, every dependency pointing inward](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/rings.png)](https://theyard.stevenstout.biz/api/docs/diagrams/rings)

*A preview. [Open the rings diagram in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/rings) to zoom in and follow it.*

Innermost first:

- **Data** (`api/TheYard.Data`). `Vehicle` and `PhotoEntry`, exactly as the dataset has them. No behaviour and no dependencies. It is the shared kernel every ring may read. A vehicle goes out on the wire under the dataset's own names as `VehicleView`, built in `VehicleWire.cs`: every dataset field except the reserve amount, plus the facts the server derives. `BidView` is built the same way, in one place.
- **Domain** (`api/TheYard.Domain`). The rules, in nine files: `BidRules`, `AuctionSchedule`, `AuctionClock`, `StandingRules`, `VehicleFilter`, `VehicleOrdering`, `VehicleSearchIndex`, `PhotoGallery` and the `Fnv1a` hash the schedule is derived from. Pure functions of their inputs and the clock they are handed.
- **Application** (`api/TheYard.Application`). The use cases and the ports. `Auction` composes one store's catalogue, everybody's bids and the simulated room in the order the rules need. `IVehicleSource`, `IPhotoManifestSource` and `IBidStore` are the auction's ports; `IActivityStore`, `ILogStore`, `IMachineHistory`, `IResetLinks`, `IEmailSender`, `IStoreExperiment` and the rest are the operator's.
- **The adapters** (`api/TheYard.Infrastructure`, `api/TheYard.Infrastructure.Cosmos`). Each implements the ports against one store: EF Core over Azure SQL Database or SQLite, and the Cosmos DB SDK. The host uses a few of their types where it builds or measures a store: the shared user `YardUser`, the `StoreLoad` reading and the `CosmosStore` the composition root creates. No endpoint holds a database context or a Cosmos DB client, and a test holds that.
- **The host** (`api/TheYard.Api`). Endpoints read a request, ask Application and write the answer. `Composition/` is the composition root, where every service is registered, and `Program.cs` is a table of contents for it.

The front end keeps the same shape in `src/`: components use hooks, hooks use `src/lib`, and `src/lib` imports nothing from React (ADR: The React configuration, explained for a new developer).

## How the build holds them

`api/TheYard.Tests/OnionTests.cs` reads the compiled assemblies with NetArchTest (the maintained fork, NetArchTest.eNhancedEdition). It sees method bodies and the types the compiler writes for lambdas and async methods, so a dependency hidden in a handler's body fails as surely as a using line would. A failure names the type and the dependency it found. The ring rules select by assembly and then by namespace, so a type in a nested folder still belongs to its ring. They allow the .NET base class library (BCL), the `System` namespaces, in every ring, and two rules take the disk and the network back out of it for Domain and Application, and the clock out of Domain. The two endpoint rules select by name, every class whose name ends in `Endpoints`, and the registration rule allows `Program` and classes ending in `Registration`; a handler class named otherwise would escape them, which is why the naming is part of the house style.

| Rule | What it stops |
| --- | --- |
| `Data_depends_on_nothing_outside_the_BCL` | The dataset's shapes picking up a framework. |
| `Domain_depends_on_nothing_outside_the_BCL_and_Data` | A rule reaching for a package (EF Core, the Cosmos DB SDK, ASP.NET). |
| `Domain_never_reaches_the_disk_the_network_or_the_clock` | A rule reading a file, calling the network, or reading the system clock or a random number itself. |
| `Application_depends_only_on_Domain_Data_and_the_BCL` | A use case depending on EF Core, the Cosmos SDK or ASP.NET. |
| `Application_never_reaches_the_disk_or_the_network` | A use case reading a file or calling the network. |
| `The_document_store_adapter_borrows_only_the_shared_user_from_the_relational_one` | The Cosmos DB adapter growing a second dependency on the EF Core one. |
| `Endpoints_never_reach_a_store_or_the_service_container` | A handler holding a database context, a Cosmos DB client or `IServiceProvider`. |
| `Endpoints_reach_the_auction_only_through_the_Application_ring` | A handler composing the bid services itself, in an order of its own. |
| `The_host_never_queries_a_database_itself` | A query written in the host instead of the adapter that owns the store. |
| `Services_are_registered_only_in_the_composition_root` | Wiring hidden inside behaviour. |

The rules for the host, as the build runs them:

```live path=api/TheYard.Tests/OnionTests.cs region=host
```

Each rule was watched failing before it passed. Four failed against the code as it stood at 1.0.3.67: the endpoint rules found `IServiceProvider` in the account and admin handlers and the bid services in the bid, vehicle and admin handlers; the document store rule found the Cosmos DB adapter using the relational adapter's `DatabaseState`; and the database rule found three queries in the host, one in a lambda inside the composition root. The other four already held, so each was proved by planting one forbidden reference (a logging package in Data, Domain and Application, and a registration outside `Composition/`) and watching it go red. The two rules on the disk, the network and the clock were proved the same way, by a planted file read in Domain and another in Application. An adapter can never use the host, because the host references the adapters and the compiler refuses a cycle, so that line has no test of its own. The rule table in ADR: The rules a change has to pass lists all ten.

## SOLID, and where each principle shows

SOLID is five design principles. In one line each, with the records that apply them:

- **Single responsibility**: a class has one reason to change. An endpoint handles HTTP, `Auction` orchestrates, an adapter persists, `BidRules` decides. `PasswordReset` holds the reset's steps so its handler only reads and writes (ADR: The composition root, split by job; ADR: One folder per component).
- **Open/closed**: add behaviour by adding a class behind an interface, not by editing a core class. The Cosmos DB store arrived as new adapters behind the same three ports, after one change to the ports themselves: every member became a `Task`, because the document store has no synchronous driver (ADR: The ports learn to wait). That change touched every caller once; the adapters that followed touched none (ADR: A second store on Cosmos DB, and what it costs).
- **Liskov substitution**: any implementation of an interface can stand in for another without surprises. `CosmosUserStore` stands in for Identity's EF store. `NullBidStore` stands in for a real bid store in the tests and in production too: a backend whose store has not come up yet bids against it, so bids work and are forgotten at restart until the store attaches. That is a deliberate limit, written down in ADR: The relational store. Where substitution is not complete it is written down below (ADR: Accounts on a document store).
- **Interface segregation**: interfaces shaped to what a caller needs. The auction's three ports are separate because the catalogue, the photos and the bids are read by different code at different times. `IStoreExperiment` has one method because the Admin tab asks one question. Its result is shaped by the one experiment that exists, a partition-key comparison on Cosmos DB, so a second kind of experiment would reshape it.
- **Dependency inversion**: the inner ring owns the interface and the outer ring implements it, and code receives what it needs through its constructor. Application declares `IBidStore`; `EfBidStore` and `CosmosBidStore` implement it; `Composition/StoreRegistration.cs` decides which one a store gets (ADR: The relational store).

## What is kept on purpose

These were found in the review that wrote this record and left as they are. Each is the right size for this codebase, and each says what would change it.

- **One shared user entity across both adapters.** `YardUser` is an Identity type, so it cannot live in Application without Application taking the Identity package. The Cosmos DB adapter references the relational adapter for that one type, and a test allows that type and nothing else. A small `TheYard.Identity` project would remove the arrow; it would add an eleventh project and buy nothing at runtime, so it waits until a third store needs accounts.
- **The backend exposes its store to the host.** `Backend` carries `CosmosStore` and the relational context factory, because the composition root builds them and the Admin tab's readings and the performance proof measure them. Endpoints never use them, and the tests hold that. A port per reading would add interfaces for code that only observes.
- **The email sender sits in the host.** `AcsEmailSender` implements `IEmailSender` over the Azure Communication Services SDK in TheYard.Api, using the credential rule the Cosmos DB adapter owns. A third adapter project for one class buys nothing at runtime.
- **The cost history waits in Application.** `ICostHistory` and its only implementation, `CostsInMemory`, sit in Application with no adapter yet, because the port is where a kept history would plug in: one adapter and one line of registration (ADR: What Azure charges).
- **Host concerns live in the host.** Health checks, the Admin tab's readings, the cost card, served documents and the activity collector read what the host keeps or what Azure reports. They encode no auction rule, so they belong outside Application.
- **Ids as strings, money as whole dollars.** No value objects for ids or amounts: the wire uses the dataset's own names and types, every amount is a whole-dollar `int` that the rules add whole increments to, and there is no repeated validation a value object would remove. A second currency or cents would change this.
- **Exceptions where the code already uses them.** A refused bid is a typed `BidOutcome`. Infrastructure failures surface as exceptions that one handler turns into a problem document (ADR: Error handling, one shape everywhere). Rewriting either into the other would add churn and no safety.
- **The server's clock is a static.** Endpoints read the server's UTC clock through `Clocks`, `Clocks.Now()` for the auction and `Clocks.UtcNow()` for a stamp or a reporting window, and hand it inward. Domain and Application take the clock as a value, which is what lets their tests fix time. Injecting a `TimeProvider` into every handler would add a parameter that tests do not need.
- **Two read and write ports not split.** `IActivityStore` and `ILogStore` each have one writer and one reader. Splitting them would double the interfaces and allow no new swap, so they stay whole until a caller needs only half.
- **Known gaps in substitution, written down.** The Cosmos DB user store does not move the email claim when an address changes, and accepts and drops Identity's cancellation token. Nothing in the site changes an email address today. The catalogue's synchronous accessors wait on a load the host has already finished (ADR: The ports learn to wait).

## When not to build it this way

The rings pay for themselves here because there are two stores behind the same rules, a host with an operator's tab, and a test suite that runs the rules on their own. A smaller job pays the cost and gets none of that.

- **A small CRUD service** over one table: one project, a minimal API, and EF Core called from the endpoint. Add a ring the day a second store or a rule worth testing alone appears.
- **A spike or a proof of concept**: one file. The point is to learn something quickly, and the code is thrown away.
- **A script or a scheduled job**: no layers at all. A function that reads, changes and writes is the clearest shape.

The Shed, a file browser built the same way on another company's starter project, takes the middle road: one project with its rings as folders, held by the same kind of tests by namespace.

## Addendum, 2026-10-04: the wire only copies

A vehicle's status and its least next bid used to be worked out in `VehicleWire`, in the host, from
the Domain's schedule and bid rules. They now sit on `StandingVehicle` in Application, beside the
standing price and the sold flag the auction already hands out, and `VehicleWire` copies them. The
host composes nothing about a vehicle; `AuctionTests` holds that the two facts are the rules' own.

## Addendum, 2026-10-17: the two ports carry more reads than this said

"What is kept on purpose" says `IActivityStore` and `ILogStore` each have one writer and one reader. They do not, and have not for some time: `IActivityStore` carries `RecordAsync` beside `AvailabilityAsync`, `HoursAsync` and `VisitorsAsync`, and `ILogStore` carries `AppendAsync` beside `AvailabilityAsync`, `QueryAsync`, `CountAsync` and `RingAsync`. The ruling it supports is unchanged, and is the reason this is an addendum rather than an edit: each port is one writer and the reads its own callers ask for, splitting them would double the interfaces and allow no new swap, and they stay whole until a caller needs only half. Found by a reader of the SOLID page on the way to shipping it, which is what a page written against the code is for.

## Addendum, 2026-10-18 (1.0.3.163): each letter in plain words first

The SOLID Principles page opened with one paragraph that ran the five habits together, so a reader
who did not already know the acronym could not match a habit to its letter. The page now opens with a
drawing, `docs/images/solid.svg`, that takes the letters one at a time: the name, one plain sentence,
and one line on where TheYard shows it, the same examples the page's table holds. Under it, one line
per letter in words, and why the five matter. The drawing has its own page like every other, and the
browser test that opens the page holds the drawing's link and the five lines.

## Where it sits

Every ring: this record is the map of all of them and of the tests that hold them, and it serves all five SOLID principles by naming where each one shows and where it stops. It cost a new test package (NetArchTest), ten tests and a pass over every record. A codebase with one store and no rules worth testing alone would be built flat instead, as the section above says.

## Addendum, 2026-10-20 (1.0.3.193): PasswordReset became handlers

The single-responsibility line above names `PasswordReset` as the class that holds the reset's steps. From 1.0.3.193 those steps are `ResetPasswordHandler` and `ResetLinkMaker` in the Application ring, behind the accounts port (ADR: The onion cut into vertical slices, rule 7); the principle is unchanged and now holds one ring further in.

## Files

- [`api/TheYard.Tests/OnionTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/OnionTests.cs): the ten rules.
- [`api/TheYard.Application/Shared/Auction/StandingReader.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Shared/Auction/StandingReader.cs): the use cases the endpoints ask.
- [`api/TheYard.Domain/Shared/Auction/StandingRules.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/Shared/Auction/StandingRules.cs): the one rule for raising a price.
- [`api/TheYard.Application/Shared/Auction/Ports.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Shared/Auction/Ports.cs): the auction's ports.
- [`api/TheYard.Api/Shared`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Api/Shared): the composition root.
- [`docs/images/rings.mjs`](https://github.com/SteveStout/TheYard/blob/main/docs/images/rings.mjs): the drawing.
- [`docs/images/solid.mjs`](https://github.com/SteveStout/TheYard/blob/main/docs/images/solid.mjs): the SOLID letters, one at a time.

## Addendum, 2026-10-20 (1.0.4.0): the onion cut into vertical slices

The rings are unchanged, and each now carries the same feature folders: a slice is `Features/<Slice>` in the host with a `Module` that registers and maps it, and, where it has use cases, `Features/<Slice>` in the Application ring with its handlers and ports (ADR: The onion cut into vertical slices). What the letters gain from it: single responsibility now holds per request, because each route asks one handler with one `HandleAsync`; dependency inversion holds per slice, because a handler reaches its store, its cards or its accounts through a port the slice declares and an adapter implements; and open and closed holds for the host, because a new slice is a folder and a `Module`, with no line added to Program.cs. `SliceTests`, `HandlerRuleTests`, `CompositionRootTests` and `OnionTests` hold the shape.

## Addendum, 2026-10-21 (1.0.4.24): the rings refuse the breaks they exist for

A review of 1.0.4.17 planted six violations, one per ring, and every architecture test passed with them in: a Data record with a rule and a `File.ReadAllText`; a Domain class reading `Stopwatch`, starting a `Process` and finding an adapter by `Type.GetType`; an Application class reading the disk through `DirectoryInfo` and `FileInfo`; a second buy-now rule inside `EfBidStore`; a Cosmos DB adapter opening `SqlConnection`; and a static route class in Features/Docs named `SearchRoutes` that opened `SqliteConnection` and added a bid increment in the host. `System` is allowed whole in every ring, so the rules held only what they named.

From 1.0.4.24 they name more, and the six planted files are each red by name (the run is in mentor\THEYARD-MASTER-DONE-2026-10-21.md). `OnionTests`: the disk and the network now include `FileInfo`, `DirectoryInfo`, `FileSystemInfo`, `System.IO.Compression`, `System.IO.MemoryMappedFiles` and `System.IO.Pipes`, and Data is held to them as Domain and Application are; the machine (`Random`, `TimeProvider`, `Environment`, `Stopwatch`, `Process`) is held out of Data and Domain, and an IL scan refuses `Type.GetType`, `Assembly.Load` and `Activator` there; `Data_has_no_behaviour` allows a Data type accessors, constructors and what the compiler writes for a record, and nothing else; `Infrastructure_adapts_and_decides_nothing` refuses the web in either adapter beyond Identity, and an adapter method that compares money (a parameter named for a price, a bid or an amount, or a read of a vehicle's price, beside a comparison in its IL); the Cosmos DB adapter's borrowing rule adds `Microsoft.Data` and Identity's Entity Framework stores; the host's query rule adds `Microsoft.Data.Sqlite` and `System.Data.Common`'s commands and connections; and an endpoint class is any type that maps a route (calls a `Map` method of the route builder), not only one named `*Endpoints`. A route delegate or a handler that takes `IConfiguration` is refused: the two that did, the reset link's and the About page's origin, now read `SiteHosts.LinkOrigin`, which carries `Site:Url` bound once by `SiteSettings`.

## Addendum, 2026-10-21 (1.0.4.24): a third store is one line, and three hidden dependencies arrive by constructor

The store keys were written as "sql" and "cosmos" in eleven files across the host and the Application ring, so a third store was an edit in every one. From 1.0.4.24 they are written once, in `StoreKeys` (Application, so the use cases' logs key by it too), and every registration, card and route reads it; `SliceTests.Rule_3_a_store_key_is_written_once_in_StoreKeys` reads the source of both assemblies and refuses a store key anywhere else (the IL cannot tell, because a constant is copied into each place that uses it). Three dependencies a test could not hand in now arrive by constructor: `MarketService` takes the `Random` that shuffles the room's candidates, so a test seeds it; `BidService` has no constructor that makes its own `MemoryBidStore`; and `TokenIssuer` keeps one `JsonWebTokenHandler` where three methods each built their own.

What this rung of the review also asked and is not done here: the 300-line rule counted per type across partial files, with `InventoryService`, `AdminEndpoints`, `CosmosStore`, `BidService` and `ProofRunner` split by job; `Backend` made immutable; one diagnostics answer in place of `LogsOperations` and `LogsStatements`; one Availability rule for every memory double; `AccountPolicy` bound from configuration; the remaining static state registered. Each is a lane of its own, named in mentor\THEYARD-MASTER-DONE-2026-10-21.md.

## Addendum, 2026-10-23 (1.0.4.39): Interface Segregation is a rule, through CQRS

The I in SOLID was held by review until this version. From 1.0.4.39 every request is a query or a command (ADR: A request is a query or a command), and a query handler may not be handed a port whose name ends in `Store` or `Writer`, so a port a query needs half of is split: `IBlogReader` under `IBlogStore`, `IAccountReader` under `IAccountStore`, `ILinkOpensReader` under `ILinkOpens`, and the auction's three parts each behind a reading and a writing interface (`IStockReader`; `IBidReader` and `IBidWriter`; `IRoomReader` and `IRoomWriter`). `CqrsTests` holds it. The three services this record named as the use cases (`InventoryService`, `BidService`, `MarketService`) and `Auction`, which composed them, are gone: the bodies moved whole into `Stock`, `BidBook` and `Room`, the composition into `StandingReader` and the bid handlers, and the regions the records above quote moved with them under the same names. The per-type 300-line rule this record's addendum of 1.0.4.24 left open still waits for its version; `BidBook` and `Stock` are its first two cases.

## Addendum, 2026-10-23 (1.0.4.47): the 300-line rule counts a type, not a file

The addendum of 1.0.4.24 left this open and the one of 1.0.4.39 named its first two cases. From 1.0.4.47 `FileShapeTests.No_production_type_runs_past_three_hundred_lines_across_its_parts` adds up every top-level C# type under `api/` across all of its partial files, from the doc comment above it to its closing brace, and fails the build past 300. A partial class cut into five files of 150 lines was still one class of 750 doing more than one job, and the file rule could not see it.

Nine types were over at 1.0.4.46, and each was cut by job into types of its own rather than into more partial files:

| type at 1.0.4.46 | lines | the job taken out, and where it went |
| --- | ---: | --- |
| `Stock` | 771 | the choice of store or list and the fill (`StockSource`), the store's answers through the five-second cache with and without the moved vehicles (`CataloguePages`), the filter in the store's spelling (`CatalogueFilterResolver`) |
| `CosmosStore` | 568 | the client and the credential (`CosmosConnection`), the container check, seed and probe (`CosmosStoreStartup`, handed out as `Starting`), the wrapper that times every call and writes its charge (`CosmosOperationLog`, handed out as `Recorder`) |
| `CosmosVehicleCatalogue` | 562 | the kept places of every order (`CosmosCatalogueKeysets`) and the live segment's (`CosmosCatalogueLive`) |
| `EfVehicleCatalogue` | 515 | the typed words' table (`EfCatalogueWords`) and the fill (`EfCatalogueFill`) |
| `YardDbContext` | 513 | the column names (`YardColumns`) and each table group's model (`StandingsModel`, `CatalogueModel`, `BlogModel`, `ActivityModel`) |
| `BidBook` | 468 | the standings it holds and reads (`BidLedger`) and the pure fold of a bid into a standing (`StandingFold`); `BidBook` keeps the writes and the gate |
| `AdminEndpoints` | 445 | one route class per card family (`AdminActivityRoutes`, `AdminCostRoutes`, `AdminFollowsRoutes`, `AdminObservabilityRoutes`, `AdminEvidenceRoutes`, `AdminUsageRoutes`); `AdminEndpoints` keeps the map |
| `ProofRunner` | 406 | the timed requests a round makes (`ProofRequests`) |
| `TelemetryReader` | 317 | the query text (`TelemetryKql`) and the answer's shape (`TelemetryShape`) |

Nothing a visitor reads changed: every cut moved bodies whole, the regions the records quote moved with them under the same names (the live fences were repointed), and the full suite passed after each cut. The largest type is now 294 lines (`RequestPipeline`). The test only counts; what a job is stays a judgement, made here one type at a time with the reason in each new file's header.

## Addendum, 2026-10-29: the clock is handed in, and this is why that changed

The "Known trade-offs" above says the server's clock is a static, that endpoints read it through
`Clocks` and hand it inward, and that "injecting a `TimeProvider` into every handler would add a
parameter that tests do not need". That was true of the handlers and it is still true of them.
It stopped being true of everything else.

**What changed is the amount of code that is not a handler.** When that line was written, the
clock was read by endpoints and passed inward as a value, and the only things holding a time of
their own were the auction's own windows. Since then the host has grown rings it keeps in memory
with a time on each entry, two stores' adapters that stamp a row they write, a nightly lane, a
plan recorder and seven resource cards that each decide which hour they are reading. None of
those is handed a clock by an endpoint, because none of them is on a request path. They read
`DateTimeOffset.UtcNow` where they stand, and the count of places doing that went from 46 to 57
in the fortnight to 29 October (ADR: The numbers that may only fall).

**Two clocks in one codebase is the cost, and the tests are where it is paid.** `TimeProvider`
was already handed in across 43 files before this version, so the codebase had both shapes at
once: the half that can be faked and the half that cannot. A reader cannot tell which a class is
without opening it, and the half that cannot be faked is the half whose behaviour at a day
boundary, at a daylight-saving change or at an expiry nobody has written down.

**So, from 1.0.4.118:** `DateTime.Now`, `DateTime.UtcNow` and `DateTimeOffset.UtcNow` belong in
a composition root, which is a `Features/<Slice>/Module.cs`, a file under `Shared/Wiring/`,
`Program.cs`, or the `Clocks` seam itself. Everything else is handed a `TimeProvider`, by
constructor, named `clock`, and the real one is `TimeProvider.System` written at the
registration. `Clocks` keeps its static and its job: it is the auction's clock, read by an
endpoint and passed inward as a value, which is the shape this record defended and still the
right one for a request that must not see two different nows.

**What this version did, and what it did not.** The two stores' adapters that stamp a row (the
reset link's expiry, the orphan claim's age, the two Cosmos history readers' day boundary, the
relational statement ring, the job hunt Function's tracker rows) and the three things the host
keeps in memory with a time on them, plus `HostStart`, take their clock by constructor. The
count went 57 to 45. The rest is named in the next rung's handoff, and three of them cannot be
done until they are split: `PlanReader`, `TelemetryReader` and `ProofRunner` each take five
dependencies, and a sixth is the number that says a class has more than one job.

**A parameter is not a seam, and the build says so.** A class with a primary constructor that
takes a `TimeProvider` and never reads it is `CS9113`, an error here under `-warnaserror`, so
the laziest version of this change does not compile. What the build cannot see is a class that
reads the clock it was handed in one place and the real one in another, so `ClockSeamTests`
moves a clock the test owns and insists the stamp follows it. That case was proved by planting
exactly that shape and watching it fail.

## Addendum, 2026-10-29 (1.0.4.120): six is the line, and three ways a class gets there

The rung above left `PlanReader`, `TelemetryReader` and `ProofRunner` waiting on a split, so this
version is about the S rather than the D: how many things a class may be handed before it is doing
more than one job. The line is six, held by a count rather than a yes-or-no test, because one more
parameter is never the moment anybody objects (ADR: The numbers that may only fall, the addendum on
counting a constructor's width). The practices page gained a section with the samples, and three
things learned here belong in the record.

**A count of commas is not a count of parameters.** The rule read five commas on the declaration's
line, so a generic type argument's own comma made three narrow classes look wide and a wrapped
parameter list hid seven that were. The fix is the same one the cancellation rule got: read the
whole signature, split it where a reader would. The lesson repeating twice in four versions is the
general one, which is that a measurement of this codebase is a claim about this codebase and gets
checked against it before anything is paid down.

**There are two honest ways to narrow a constructor and they are not the same.** Splitting by job
is the first: the host's store announcement was handed seven things to do two jobs, attaching the
loggers and saying what came up, so each is a type of its own and what is left is the order between
them. Grouping by concern is the second: the four rings this process keeps were handed over one at
a time to classes whose every answer reads the rings and nothing else about them, so the rings
travel as one collaborator now. The first removes a reason to change; the second removes a
parameter. Grouping parameters that have nothing to do with each other would leave the reasons to
change where they were and only tidy the signature, which is worth refusing by name because it is
the cheap way to make this number fall.

**Splitting a class has one failure the compiler cannot see.** Every `new X(...)` whose arity
changed is a build error. A type the service container activates by name is resolved at run time,
so a part split off a class and registered nowhere fails when something first asks for it, which on
this project meant a red gate on a store boot the shipping laptop cannot run (1.0.4.118). So
`ConstructorWidthTests` resolves every part off the real host, and the rule for this rung is that a
split and its registration go in the same commit as each other.

## Addendum, 2026-10-29 (1.0.4.121): the three that were waiting, and a third way to narrow

The addendum above named `PlanReader`, `TelemetryReader` and `ProofRunner` as waiting on a split
before they could be handed a clock. This version splits all three and hands each one its clock in
the same commit, which is the only order that works: each sat at exactly five parameters, so a
clock handed to any of them before the split would have made it wide and the rung above would have
gone backwards to let this one move.

**All three were keeping an answer for a window, which is why all three needed a clock.** The
plan's layout is good for an hour, the telemetry reading for a minute, and the proof will not start
a run within a minute of the last one finishing. A window is a question about now, so each of them
was reading `DateTimeOffset.UtcNow` where the thing it was asking about was time.

**The split in each case put the window on one side and the asking on the other.** `PlanLayout`
holds which plan this site is on and which apps share it, which is the only part of the plan's
reading that remembers anything, and `PlanArm` is the one door to Azure Resource Manager both parts
read through. `TelemetrySource` holds the token, the query and the POST, so what is left in
`TelemetryReader` is how long an answer is good for. `ProofRunner` keeps the gate, the cooldown and
the last result.

**A third honest narrowing belongs beside the two above: hand over the collaborator, not its
parts.** `ProofRunner` took a `SqlRingBuffer` and a `StoreRingBuffer` only to build the
`ProofRequests` that measures one request out of them. It takes the `ProofRequests` now. That is
not grouping parameters: a class stopped doing a job that was already another type's, which is the
same reason as the first way and not the second. It also removed the one thing no test could reach
past, because a class that builds its own collaborator cannot be handed a different one.

**The tests moved a clock rather than reading a signature.** Each of the four cases sets a clock to
a moment no real clock will be at, reads inside the window and outside it, and counts what actually
went out to the service; the proof's own case asserts that the stamp on a finished run is the
handed clock's moment. The shape they exist to catch was planted and watched fail: a windowed read
left on `DateTimeOffset.UtcNow` while the stamp beside it used the handed clock. Worth writing
down, because the first plant was cruder and would not build at all: a constructor parameter that
is read nowhere is `CS9113`, which this build turns into an error, so a class handed a clock it
ignores entirely cannot ship here. The shape that can ship is the mixed one, and that is the one
the tests hold.

`clock_outside_composition` is 35 from 45, lowered in this same commit. `wide_service_classes` is
unchanged at 8: none of these three was ever on that list, and each is still under the line with
its clock.

## Addendum, 2026-10-29 (1.0.4.122): four files cut at the line cap, and the two that are not

The 300-line rule has an early warning beside it: `files_within_10_of_cap`, the files within ten
lines of the cap, which is the list of files whose next edit would be the one that had to split
them. It was six. The rule says a file that close is cut by job *before* anything is added to it,
and this version cuts four of the six:

- **`RequestPipeline.cs`** (300 lines, at the cap) keeps the order, which is the only thing in it
  that was ever a decision, and the eleven steps now sit in three files: `RequestRecording.cs` for
  what every answer is recorded and stamped with, `ResponseKeeping.cs` for what a browser and the
  edge may keep, and the pipeline itself for the order and the four steps that are only a line each.
- **`ShareCard.cs`** (293) keeps the rows and the chart the card draws; `ShareKeeping.cs` holds what
  a kept hour carries and the words it is read back in. One changes when the card changes, the other
  when Azure's record or the bill's shape does.
- **`NetlifyEdge.cs`** (292) keeps the reader that asks Netlify; `NetlifyDeploys.cs` holds what the
  deploy list it answers with says, every member pure and handed the JSON the reader already has.
- **The Shed's `FileBrowser.cs`** (298) keeps carrying out an operation; `BrowsePaths.cs` holds
  resolving a path, refusing when nothing is there, and describing an entry. Every method on the new
  type answers a question about a path and none of them changes anything, which is the line the cut
  follows (ADR-002 in that project, the addendum of 2026-10-29).

**Two are left, each for a reason worth writing down rather than a reason to hurry.**
`StoreRegistration.cs` (298) is one `await`ed method whose two halves, the relational backend and
the document one, read a dozen locals each; cutting it honestly means a type to carry those locals,
which is a version of its own rather than the tail of this one. `NightlySource.cs` (294) is also one
of the eight classes still handed six or more things, and it keeps a `volatile` field both halves
read, so it should be cut once, by the instalment that takes its width, and not twice.

**A split like this has a failure the compiler cannot see either, and it is the order.** Two of the
pipeline's steps are not next to each other on purpose: the timing is outside the exception handler
and the error record is directly inside it. The first cut of this split put them together, which
reads better and is wrong, and `ExceptionHandlerTests` failed at once with an empty error list. So
`RequestRecording` is two methods rather than one, the file that holds the order is the file that
puts them in their places, and a test now reads that order where a reviewer reads it.

**The records follow the code in the same commit.** Nine live fences across five documents quoted
regions by file, and a region that has moved to another file renders as a note where the record
promised code; `LiveSampleCoverageTests` names every one of them, which is how they were found.

`files_within_10_of_cap` is 2 from 6, lowered in this same commit. No file in the six projects or
the sample is over 300 lines, and none of the four cuts changed what anything does.
