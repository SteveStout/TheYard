# TheYard

A used-vehicle auction platform. React + TypeScript (Vite) frontend, .NET minimal API
backend in onion architecture.

## Before you change anything

Read the record that governs it. The decisions are `docs/decisions/ADR-*.md`, served from the running app under
Decision Records; every other document lives in the folder named for its sidebar section, and a new one goes
there too (ADR: One folder per sidebar section). The ones a test enforces are listed beside their test in
`docs/decisions/ADR-075-the-rules-a-change-has-to-pass.md`. A change that contradicts a record changes the record
first, as an addendum that says when it stopped being true rather than an edit that makes it look like
it was always this way. The test goes in the same commit as the change, and the one gate runs
green before anything rolls (ADR-068). Any build warning is red.

## Architecture, and it is not negotiable

Ten projects; six form the onion, and among them dependencies point INWARD only.
`api/TheYard.Tests/OnionTests.cs` fails the gate when one points outward, or when Domain or
Application reach the disk, the network or (Domain) the clock (ADR: Onion and SOLID, how this
codebase holds them):

- `api/TheYard.Data` - pure records, ZERO dependencies, ZERO behavior. If it computes anything it does
  not belong here.
- `api/TheYard.Domain` - the rules. Depends only on Data.
- `api/TheYard.Application` - use cases and ports. Every request is a query or a command with one handler (ADR-115); the auction is
  `Stock`, `BidBook` and `Room`, each behind a reading and a writing interface, and `StandingReader` is how a query reads a vehicle; the
  auction's ports are `IVehicleCatalogue` (the store's hundred thousand, queried a page at a time),
  `IVehicleSource` (the seed, which fills it), `IPhotoManifestSource` and `IBidStore`, beside the
  operator's ports (`IActivityStore`, `ILogStore`, `IStoreExperiment` and the rest). Depends on Domain.
- `api/TheYard.Infrastructure` - the relational adapters: EF Core over Azure SQL Database or SQLite, the
  JSON seed readers, the synthetic scale-up decorator, the Identity user entity. Implements the ports.
- `api/TheYard.Infrastructure.Cosmos` - the second adapter in the same ring: the same ports and an
  Identity user store over Azure Cosmos DB on the SDK, referencing Infrastructure only for the shared
  user entity.
- `api/TheYard.Api` - host and endpoints. One folder per slice under `Features/`, each with a `Module.cs`
  that registers and maps it; what more than one place uses in `Shared/<Concern>/`, one folder per concern
  named in ADR-114's table; `Program.cs` is the only file at the root, and it registers the shared parts by
  name and loops over the modules. Read ADR-107 (The onion cut into vertical slices), ADR-114 (The host
  root holds one file) and ADR-115 (A request is a query or a command) first; ADR-116 (People who have visited, counted once) for the visitor count; ADR-117 (One host per process, the rings under them) before touching `functions/`; ADR-118 (The campaign sends, the model writes rows) before touching the campaign; ADR-119 (The modular monolith) before adding a project, a deployable or a module; ADR-121 (The Foreman, a place to start) before touching `Features/Admin/Foreman`. The four records and their tests in both processes are one table in docs/app-architecture/ARCHITECTURE.md. NO business logic in endpoints.

Beside the six: `api/TheYard.Database` (the SQL Server schema as a DACPAC, the authority for the relational schema),
`api/TheYard.Migrations.Sqlite` (the SQLite schema's history), `api/TheYard.Experiment` (a console tool
for the partition key experiment), and `api/TheYard.Tests`. One process runs BOTH stores side by side and
picks one per request from the `X-Yard-Store` header, else by the name the request arrived on
(`Shared/Instance/SiteHosts.cs`, ADR-105); one web app serves both sites and The Shed (ADR-066, ADR-105).

Frontend keeps the same discipline: `components` -> `hooks` -> `lib`. **`src/lib` imports nothing from
React.** Every component has a folder of its own, `src/components/<section>/<Name>/`, holding its `.tsx`,
its `.module.css` when it has one, and an `index.ts` that re-exports it, so an import reads `components/<section>/<Name>`; a
component another section renders lives in `shared/`, and `src/lib` and `src/hooks` stay where they are
(ADR: One folder per component). `src/app` is the shell: `App.tsx` reads like a table of contents, and what
the app knows is one hook per file in `src/app/hooks`, named for what it gives back. `src/library` is the
documents the site serves, as data, and the window one opens in. **Every file in those two folders opens with
a Does / Does not / Used by header, one job per file, 300 lines at most**; `FileHeaderTests` holds all three
and checks "Used by" against the real importers (ADR: The React configuration, explained, the addendum on the
split).

## Rules that must survive any change

- **Derive, do not store.** Auction windows derive from the item id via FNV-1a hash. Status derives from
  the window and the clock. Nothing schedule-related is persisted, with one exception since
  1.0.3.178: the slot, the window's start and end as offsets from the day's anchor, is written beside
  each vehicle in the store so the store can order a page by auction time. It is a copy of the hash's
  answer, `CatalogueSlot.For` is the only code that makes it, and `StoreCatalogueTests` holds all
  hundred thousand to the window the hash gives on any day (ADR: The store does the work). Status is
  still never stored.
- **The server owns every derived fact, and the clock.** The server computes windows, status and
  `min_next_bid` on its own clock: now, and the UTC midnight that began the day, one anchor for every
  visitor (`AuctionClock.Utc`). No request names a day; an `anchor_ms` from an old page is ignored.
  **Never re-implement auction math in TypeScript**: two copies of the schedule drift apart, first on a
  daylight-saving change, and a caller that picks the anchor sees a different auction from everybody
  else (ADR: Three readers with no memory of the project).
- **The wire is snake_case** under the dataset's own names. A vehicle goes out as `VehicleView`
  (`VehicleWire.cs`): every dataset field except the reserve amount, plus the six facts the server
  derives. A field added to `Vehicle` is added there too; `VehicleWireTests` fails until it is.
- **Empty is valid, null is the error.** Never return null for a collection.
- **300 lines a file, and a type.** No production C# file under `api/`, and no C# type counted across all of
  its partial files, runs past 300 lines; a longer one is cut by job into types of its own, never into more
  partial files (`FileShapeTests`, ADR-088's addendum of 1.0.4.47).
- **Money is whole dollars as `int`**, because the dataset's prices are whole dollars and every rule adds
  whole-dollar increments; nothing needs cents. **Every derived or recorded instant is milliseconds since
  the epoch as `long`, UTC** (the clock's anchor, the window's start and end, a bid's `at_ms`), the same unit the
  browser's clock uses; the dataset's own `auction_start` date string passes through as the dataset has it
  and nothing is derived from it.
- Bid rules live only in `TheYard.Domain/Shared/Auction/BidRules.cs`. A bid at or above buy-now wins AT the buy-now
  price, and that check runs BEFORE the increment check. A vehicle anybody has bought is SOLD, to
  everybody, on both sites: that check runs before all the others, the standing the store owns supplies it
  (read by `BidBook` under its gate and written back with a compare-and-set; `BidBook.IsSold` is the
  listing's cached copy), `StandingReader.FindAsync` and `StandingReader.SearchAsync` hand every vehicle out with its
  sold flag beside it, and the wire says `sold` on every vehicle. A listing or a rule that forgets it recreates the second buyer (ADR: Accounts and per-user
  bids, addendum). Raising a vehicle's shown price over a bid, and whether the reserve is met, live only
  in `TheYard.Domain/Shared/Auction/StandingRules.cs` (`RaisedTo`, `Reserve`).

## Quality ratchet

Fourteen counts describe the shape of this codebase and live in `.github/quality-ratchet.json`:
declarations with no `///` summary, service classes taking six or more dependencies, async methods
with no `CancellationToken`, direct clock reads outside a composition root, comments carrying a date
or a name, files within ten lines of the 300 cap, behaviour types no test names, the outward
references and ring leaks already held at zero, and two floors (the test-to-production line ratio and
the xUnit count). Run it before the gate:

```
python3 .github/quality.py . --ratchet .github/quality-ratchet.json
```

Every line must read `ok`. CI's api job and the ship gate run the same command, so a change that
makes any number worse is red. **A number you improve is lowered in the file in the same commit as
the fix**, which is the only way a limit ever moves; the step prints a notice when a number has moved
the good way and the file has not followed it. Never lower the 90 per cent line and 75 per cent branch
coverage floor to make a run green (ADR-122).

## Testing

- `dotnet test` - xUnit. Domain with fixed clocks, Application with hand-written fakes at the ports
  (no mocking framework), Infrastructure against the real dataset, integration via
  `WebApplicationFactory`.
- `npm test` - Vitest, presentation logic only.
- `npx playwright test` - end-to-end, launches both servers itself.
- **All three must pass before any commit that changes behavior.**

## Commands

- `npm install` then `npm start` runs the API and the frontend together.
- API alone: `npm run api`. Frontend alone: `npm run dev`.
- Docker: `npm run docker` builds the image and serves everything at http://localhost:8080; `npm run docker:stop` stops it. Raw commands are documented in the README.
- VS Code: `.vscode/` carries every command as a task and F5 for the API, the page and The Shed; `docs/app-architecture/VSCODE.md` is the walkthrough and `WorkspaceTests` holds the files to the tree (ADR: The editor is set up by the repository).

## Environment

Windows. **`vite.config.ts` excludes `**/api/**` from the watcher because dotnet holds locks on `obj/`
and Vite crashes with EBUSY without it. Do not remove that exclusion.**
