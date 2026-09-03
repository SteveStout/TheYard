# App Architecture

The shape of the whole application in one page: what each part owns, which
way the dependencies point, and the rules that keep it that way. The
records under this one in the sidebar explain the individual decisions;
this is the map they hang from. It is also the answer to the promise the
README made when the build started, that a written architecture and style
would exist and be enforced rather than remembered.

[![TheYard data flow: the read path from the seed file to the cards, and the write path of a bid beside it](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/dataflow.png)](https://theyard.stevenstout.biz/api/docs/diagrams/dataflow)

*A preview. [Open the data flow diagram in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/dataflow)
to zoom in and follow it. The infrastructure has [its own drawing](https://theyard.stevenstout.biz/api/docs/diagrams/infrastructure).*

## The topology, in the document

The two drawings above are pictures. This one is text: it lives in this file,
changes in the same commit as the thing it describes, and shows up in a diff
when the topology moves. That is the whole reason it is here in a format a
reviewer can read as source rather than open in an editor.

Solid lines are what serves a request today. Dotted lines are the deploy path
and the identity path, which are real but not on the request's critical route.

```mermaid
flowchart LR
  B["Browser<br/>theyard.stevenstout.biz"]

  subgraph edge["Phase 1 edge: Netlify, free tier"]
    TLS["TLS termination, Let's Encrypt<br/>rewrite proxy: /* to the origin"]
  end

  subgraph azure["Azure, resource group RG-THEYARD-SS"]
    ACI["Container Instances<br/>1 vCPU, 1.5 GB, port 8080"]
    ACR[("Container Registry")]
    MI["Managed identity<br/>id-theyard-ss"]
    AI["Application Insights<br/>appi-theyard-ss"]
    LAW[("Log Analytics<br/>log-theyard-ss, 0.1 GB cap")]
  end

  subgraph sql["Azure, resource group RG-THEYARD-SS, West US 3"]
    SQL[("Azure SQL Database<br/>sqldb-theyard-ss, serverless, free limit<br/>catalogue, photo manifest, accounts, bids")]
  end

  subgraph box["Inside the container"]
    API["ASP.NET Core minimal API, .NET 10"]
    SPA["React 19 bundle, served as static files"]
    SEED[("data/vehicles.json<br/>200 records, seeds the database on first boot")]
    FILE[("SQLite fallback<br/>only when the database is unreachable")]
  end

  B -->|HTTPS, session cookie| TLS
  TLS -->|HTTP 8080| ACI
  ACI --> API
  API --> SPA
  API -->|read once at startup, expanded to 100,000| SQL
  API -.->|when SQL is unreachable| FILE
  MI -.->|db_datareader, db_datawriter| SQL
  SEED -.->|first boot only| SQL
  ACR -.->|image pulled on every roll| ACI
  ACI -.->|IMDS token| MI
  MI -.->|Reader, Monitoring Reader| AI
  API -.->|requests, dependencies, exceptions| AI
  AI --> LAW
```

*Mermaid source. It renders as a picture on GitHub, and it is kept here as text
on purpose: it lives in this file, so it changes in the same commit as the
topology and shows up in a diff. The drawn version of the same thing, to zoom
in on, is the [infrastructure diagram](https://theyard.stevenstout.biz/api/docs/diagrams/infrastructure).
Why the renderer is not in the bundle is measured in ADR: Style, enforced.*

The database is reached as the managed identity, with no password anywhere in
the connection string, and the schema it maps to is published from
`api/TheYard.Database` rather than created by the container (ADR: The SQL Server
backend, and ADR: Data first).

The two things that are not on this picture are on it on purpose. Cloudflare
is a staged, dormant zone that cannot take over until the registrar transfer
(ADR: Front Door origin), and Azure Front Door is written in Bicep and
deliberately undeployed because the free trial forbids it (ADR: Deployment
strategy). Drawing either as though it were serving traffic would make this
diagram a wish rather than a map.

## The data, as tables

What the database actually holds, which is four tables of this application's own
plus the seven ASP.NET Core Identity brings with it. The authority for this
picture is `api/TheYard.Database`, and a conformance test holds the Entity
Framework model to it, so this diagram cannot quietly stop being true without
something failing (ADR: Data first, and the database in source control).

[![TheYard's database: the four tables this application owns with every column and type, Identity's seven, and the two relationships deliberately left unenforced](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/erd.svg)](https://theyard.stevenstout.biz/api/docs/diagrams/erd)

*A preview. [Open the database diagram in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/erd)
to zoom in and read the column types. It is drawn by
[`docs/images/erd.mjs`](https://github.com/SteveStout/TheYard/blob/main/docs/images/erd.mjs)
from a table of facts, so a column that changes is a one-line edit rather than a
drawing exercise. There is no PNG copy beside it, unlike the older two drawings:
raw.githubusercontent serves an SVG as an image, so a second file would be one
more thing to keep in step for nothing.*

The same thing as text, which is what shows up in a diff:

```mermaid
erDiagram
  AspNetUsers ||--o{ Bids : places
  AspNetUsers ||--o{ AspNetUserClaims : has
  AspNetUsers ||--o{ AspNetUserLogins : has
  AspNetUsers ||--o{ AspNetUserTokens : has
  AspNetUsers ||--o{ AspNetUserRoles : joins
  AspNetRoles ||--o{ AspNetUserRoles : joins
  AspNetRoles ||--o{ AspNetRoleClaims : has
  Vehicles }o..o{ Photos : "chosen by hash, never stored"
  Vehicles ||..o{ Bids : "no constraint, see below"

  Vehicles {
    nvarchar_64 Id PK "the seed id"
    int Seq UK "seed order, clustered"
    varchar_17 Vin "ISO 3779"
    int Year
    nvarchar_64 Make
    nvarchar_64 Model
    nvarchar_64 Trim
    nvarchar_32 BodyStyle
    nvarchar_32 ExteriorColor
    nvarchar_32 InteriorColor
    nvarchar_128 Engine
    nvarchar_64 Transmission
    nvarchar_16 Drivetrain
    int OdometerKm
    nvarchar_32 FuelType
    decimal_3_1 ConditionGrade
    nvarchar_1024 ConditionReport
    nvarchar_max DamageNotes "JSON array"
    nvarchar_32 TitleStatus
    nvarchar_64 Province
    nvarchar_64 City
    datetime2_0 AuctionStart
    int StartingBid
    int ReservePrice "null means no reserve"
    int BuyNowPrice "null means no buy now"
    nvarchar_max Images "JSON array"
    nvarchar_128 SellingDealership
    nvarchar_32 Lot
    int CurrentBid "null until the first bid"
    int BidCount
  }

  Photos {
    nvarchar_128 File PK
    int Seq UK "manifest order, clustered"
    nvarchar_32 Style "the body-style pool"
    nvarchar_256 Title "the source title"
  }

  Bids {
    nvarchar_128 UserId PK "FK to AspNetUsers"
    nvarchar_64 VehicleId PK "no FK, see below"
    int Amount
    int BidCount
    bit WonBuyNow
    bigint AtMs
    rowversion RowVersion "concurrency token"
  }

  AspNetUsers {
    nvarchar_128 Id PK
    bigint CreatedAtMs "this application's one addition"
    nvarchar_256 UserName
    nvarchar_256 NormalizedUserName UK
    nvarchar_256 Email
    nvarchar_256 NormalizedEmail
    nvarchar_max PasswordHash
    nvarchar_max SecurityStamp
    nvarchar_max ConcurrencyStamp
    bit EmailConfirmed
    bit TwoFactorEnabled
    bit LockoutEnabled
    datetimeoffset LockoutEnd
    int AccessFailedCount
  }

  AspNetRoles {
    nvarchar_128 Id PK
    nvarchar_256 Name
    nvarchar_256 NormalizedName UK
    nvarchar_max ConcurrencyStamp
  }

  AspNetUserRoles {
    nvarchar_128 UserId PK
    nvarchar_128 RoleId PK
  }

  AspNetUserClaims {
    int Id PK
    nvarchar_128 UserId
    nvarchar_max ClaimType
    nvarchar_max ClaimValue
  }

  AspNetRoleClaims {
    int Id PK
    nvarchar_128 RoleId
    nvarchar_max ClaimType
    nvarchar_max ClaimValue
  }

  AspNetUserLogins {
    nvarchar_128 LoginProvider PK
    nvarchar_128 ProviderKey PK
    nvarchar_max ProviderDisplayName
    nvarchar_128 UserId
  }

  AspNetUserTokens {
    nvarchar_128 UserId PK
    nvarchar_128 LoginProvider PK
    nvarchar_128 Name PK
    nvarchar_max Value
  }
```

*Mermaid uses underscores where SQL uses brackets and parentheses, so
`nvarchar_64` is `nvarchar(64)` and `decimal_3_1` is `decimal(3,1)`. The types
themselves are the ones in
[`api/TheYard.Database`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Database).*

Two relationships on that diagram are dotted, and both are dotted because the
database does not enforce them.

**Vehicles to Bids** has no foreign key. The `Vehicles` table holds the 200-row
seed catalogue, `SyntheticVehicleSource` expands it in memory to 100,000 by
deriving ids from it, and a visitor bids on the expanded set. A constraint would
reject 99.8 per cent of legitimate bids. It becomes correct the day the expansion
is persisted, and a test asserts its absence so that day is noticed.

**Vehicles to Photos** has no join table. A vehicle's gallery is chosen at
request time by hashing its id against the pool for its body style, so the
association is computed and never stored. `Vehicles.Images` holds the dataset's
own image URLs, which are not manifest file names, so there is nothing to point a
foreign key at.

Everything else is a real constraint. Deleting an account cascades to its bids,
its claims, its logins, its tokens and its role memberships.

The dependency direction inside the API, which is the other half of the shape:

```mermaid
flowchart RL
  Api["TheYard.Api<br/>host, endpoints, composition"]
  Infra["TheYard.Infrastructure<br/>adapters"]
  App["TheYard.Application<br/>use cases and ports"]
  Domain["TheYard.Domain<br/>rules, pure functions"]
  Data["TheYard.Data<br/>records, no logic"]

  Api --> Infra
  Api --> App
  Api --> Domain
  Api --> Data
  Infra --> App
  Infra --> Domain
  Infra --> Data
  App --> Domain
  App --> Data
  Domain --> Data
```

Every arrow points inward and none points back. `TheYard.Data` has no
dependencies at all, which is what makes it safe for every other layer to hold
its records.

## One picture in words

A browser asks the API for a page of vehicles. The API owns the data, the
rules and the derived facts; the browser formats what it is given and
counts down clocks. Nothing about an auction is decided twice.

## The layers, and which way they point

The API is an onion: every arrow points inward, and the innermost layer
knows nothing about the ones around it.

| Project | Owns | Depends on |
| --- | --- | --- |
| `api/TheYard.Data` | The plain records: `Vehicle`, `PhotoEntry`. No logic. | nothing |
| `api/TheYard.Domain` | The rules: auction schedule and clock, filter, ordering, bid rules, photo gallery, FNV-1a. Pure functions and records. | Data |
| `api/TheYard.Application` | The use cases: `InventoryService`, `BidService`, and the ports (`IVehicleSource`, `IPhotoManifestSource`, `IBidStore`) they read through. | Domain, Data |
| `api/TheYard.Database` | The SQL Server schema, hand written, compiled to a DACPAC. The authority for what the database is. | nothing |
| `api/TheYard.Infrastructure` | The adapters: EF Core over Azure SQL Database or SQLite, the JSON readers that seed it, the synthetic scale-up decorator. | Application, Domain, Data |
| `api/TheYard.Migrations.Sqlite` | The SQLite schema's history, applied by the process that uses it. | Infrastructure |
| `api/TheYard.Api` | The host: composition, endpoints, serialization, static files, the served documents, observability. | all of the above |
| `src/` | The browser: rendering, formatting, countdowns, URL state, one fetch seam. | the wire only |

The test for whether a layer is earning its place is whether something can
be swapped at its seam. Three things have been: the 100,000-record scale-up
is a decorator on `IVehicleSource` and nothing above it changed, the test
suite hands the same services in-memory fakes, and the catalogue moved from
JSON files to SQLite without one line changing in Application or Domain
(ADR: The relational store).

```live path=api/TheYard.Application/Ports.cs region=ports
```

## The rules that keep it that way

**Derive, do not store.** Auction windows, statuses, galleries and the
100,000 vehicles are all computed from stable ids. Nothing is persisted,
so nothing can drift out of date, and the same id always produces the same
answer. The seed file is never modified.

**One authority per rule.** A rule lives in `TheYard.Domain` and nowhere
else. The browser once mirrored the auction math in TypeScript and the two
disagreed twice, across time zones and then on a daylight-saving day. The
derived facts now travel on the wire (`auction_starts_at`,
`auction_ends_at`, `auction_status`, `min_next_bid`) and the browser only
formats them.

**The wire is the contract.** snake_case in the dataset, snake_case on the
wire, snake_case in the browser: nothing is renamed in transit. The
envelope is `{ total, vehicles }`; a failure is a ProblemDetails body
(ADR: Error handling). The two settings that hold that contract are in
Program.cs (ADR: Program.cs, explained).

**Endpoints bind and delegate.** A `MapGet` validates its parameters and
calls a service. If a rule appears in the host file, it is in the wrong
place.

**The address bar is the application state.** Filters, sort, the open
vehicle and the Admin tab are all query parameters, mirrored by
`src/App.tsx` and read back by `src/lib/inventory.ts`. There is no router
and no state library; Back and Forward work because the URL is the truth.

**One seam to the API.** Every `fetch` in the browser is in
`src/lib/data.ts`, with its cache, its debounce and its abort signal. A
component never fetches.

**Nothing ships untested.** Three suites, one per level: pure rules in
xunit, the browser's logic in Vitest, the real stack in Playwright. CI
runs all three on every push and the deploy will not fire without them
(ADR: The tests, explained).

## Where a change goes

| The change | Where it goes |
| --- | --- |
| A new auction or bidding rule | `api/TheYard.Domain`, with a unit test first |
| A new endpoint | one `MapGet`/`MapPost` in Program.cs, plus an integration test |
| A new data source | a port in Application, an adapter in Infrastructure |
| A new derived fact for the browser | `api/TheYard.Api/VehicleWire.cs` |
| A new API call from the browser | one function in `src/lib/data.ts` |
| A new view state | the URL, through `filtersToSearchParams` |
| A visitor preference (not a view) | `localStorage`, like the collapsed rail |
| A new document or record | `docs/`, then `DocsCatalog.cs` and `DocsMenu.tsx` |
| A new colour or spacing value | `src/styles/tokens.css`, never a literal |

## What is deliberately not here

No durable volume: there is a database now (SQLite through EF Core), but the
file lives in the container's own writable layer, so bids survive a restart
and not a roll, which ADR: The relational store is explicit about. No
authentication (one anonymous buyer). No state
library, router, component library or CSS framework. No server-rendered
React. Each of those is a decision with a record behind it, not an
oversight; ADR: Deployment strategy and the Hosting page cover the hosting
side of the same question.

## Files

- [`docs/STYLE.md`](https://github.com/SteveStout/TheYard/blob/main/docs/STYLE.md): the naming, layering and commenting rules this page's principles turn into, and the `.editorconfig` that enforces the mechanical half.
- [`api/TheYard.Application/Ports.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Ports.cs): the two ports the layers meet at.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the composition root, walked line by line in ADR: Program.cs, explained.
- [`api/TheYard.Api/VehicleWire.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/VehicleWire.cs): the derived facts that make the browser's job formatting.
- [`src/lib/data.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/data.ts) and [`src/lib/inventory.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/inventory.ts): the one seam and the URL state.
- [`docs/DATAFLOW.md`](https://github.com/SteveStout/TheYard/blob/main/docs/DATAFLOW.md): the same shape as a walk, step by step.
- [`docs/PROJECTS.md`](https://github.com/SteveStout/TheYard/blob/main/docs/PROJECTS.md): every project and folder, one line each.
