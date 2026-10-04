# Projects

Eleven pieces, listed inside-out. Each may only depend on the ones above it.

## In plain words

This page lists the eleven pieces the code is split into (the .NET projects and the React front end), from the innermost out, and says what each one holds. Each piece may only depend on the ones listed above it.

What that is worth: a developer can tell where a change belongs and what it may touch before opening a file, and the organization can swap a storage adapter without rewriting the rules above it.

## TheYard.Data

The innermost ring and the language every other layer speaks: the pure data records,
`Vehicle` exactly as it appears in `data/vehicles.json`, and `PhotoEntry` from the photo
manifest. Sealed records, value equality, zero dependencies, zero behavior. If it
computes anything, it doesn't belong here.

## TheYard.Database

The SQL Server schema, hand written, compiled to a DACPAC. The authority for what the database is.

## TheYard.Domain

The business rules, as pure functions over Data: `AuctionSchedule` derives each
vehicle's auction window from its id, `BidRules` owns increments, validation, and the
buy-now override, `VehicleFilter` is the search predicate, `VehicleOrdering` ranks
results, `StandingRules` holds the one rule for raising a vehicle's shown price over a bid
(`RaisedTo`) and whether its reserve is met (`ReserveOf`), and `PhotoGallery` picks
deterministic galleries. Everything takes its clock as
an argument (`AuctionClock`), so every rule is testable with a fixed timestamp and no
mocking.

## TheYard.Application

The use cases, and the seams. `Auction` is what the endpoints ask about the auction: it composes one
store's catalogue, everybody's bids and the simulated room (`MarketService`) in the one
order the rules need, and `BidsOf` gives a buyer's bids as `BidView`s. `InventoryService` loads the dataset once and answers
search/facet/by-id queries by composing Domain rules; `BidService` holds the buyer's
bid state, read from the database at startup and written through on every accepted
bid, and applies it *before* filtering so prices never disagree with the
UI. Both consume data through the auction's three ports (`IVehicleSource`, `IPhotoManifestSource`, `IBidStore`): the
interfaces that make Infrastructure swappable and the tests trivial to fake. Beside them sit the
operator's ports, which the Admin tab and the account flows read through: `IActivityStore`,
`ILogStore`, `IMachineHistory`, `ICostHistory`, `IResetLinks`, `IEmailSender`, `IStoreExperiment`
and the rest.

## TheYard.Infrastructure

The adapters behind those ports: EF Core over Azure SQL Database or SQLite for the
catalogue, the photo manifest, accounts and bids; `JsonFileVehicleSource` and
`JsonFilePhotoManifestSource`, which deserialize the files on disk that seed a fresh database; and
`SyntheticVehicleSource` decorates a source to expand 200 seeds into 100,000
deterministic records, proof the port design works, since nothing above it changed when
the dataset grew 500×. It also holds the Identity user (`YardUser`), the context factory and
the database readings the Admin tab shows (`ResourceStats`, `YardDatabase.PingAsync`).

## TheYard.Infrastructure.Cosmos

The auction's three ports, the operator's stores and the account store over Azure Cosmos DB, on the SDK directly, with every operation's request charge written to the store log (ADR: A second store on Cosmos DB, and what it costs). The partition key experiment the Admin tab runs is `PartitionExperiment`, behind `IStoreExperiment`.

## TheYard.Migrations.Sqlite

The SQLite schema's history, applied by the process that uses it.

## TheYard.Experiment

A console tool: seeds the 100,000-document catalogue and runs the partition key's query set in paired rounds (ADR: The partition key).

## TheYard.Api

The host and the composition root: `Program.cs` is a table of contents, one line per
step with the file that holds it beside it; `Composition/` registers every service and builds
the request pipeline; `Endpoints/` declares every API route, one file per area, and each
handler asks Application; `VehicleQueryParams` binds and validates GET parameters,
`Clocks` builds the request's clock (now, and the UTC midnight that began the day, the same for every caller), and `VehicleWire` stamps server-derived
auction facts onto each outgoing vehicle. Endpoints contain no logic, only binding and
delegation.

## TheYard.Tests

One suite per ring: Domain rules with fixed clocks, Application services with in-memory
fakes at the ports, Infrastructure against both fixtures and the real dataset, and
integration tests that boot the actual host in-memory (`WebApplicationFactory`) to
verify routes, parameters, error paths, and the full bid lifecycle, with no
running server required. `OnionTests` reads the compiled assemblies and fails the build
when a dependency points outward (ADR: Onion and SOLID, how this codebase holds them). How many tests there are is counted in the README.

## Frontend (src/)

React + TypeScript, deliberately thin. No business math runs in the browser:

- `main.tsx`: entry point, read like `Program.cs`: one line per part with its file beside it. `styles/globalStyles.ts` loads the stylesheets in order, `app/reportUncaughtErrors.ts` reports crashes no boundary sees, and `app/mount.tsx` draws `App` inside the error boundary.
- `app/`: the app shell. `App.tsx` is the composition root and reads like a table of
  contents: one line per hook, then the view the address names inside `Shell.tsx`
  (the frame), with `Header.tsx`, `Footer.tsx` and `InventoryView.tsx` beside it.
  `app/hooks/` holds what the app knows, one file each: `useAddressBar` (view state and
  URL sync, the only writer of the address bar, Back and Forward), `useNavigation`
  (opening and closing views, focus, the announcement), `useInventory` (the debounced
  fetch effect and Load More, with `useListingRefresh` beside it), `useOpenVehicle`,
  `useAccount`, `useRail` and `useRunningBuild`.
- `library/`: the documents the site serves, as data. `records.ts` (every decision
  record), `pages.ts` (every other document), `documents.ts` (the two joined, and the
  `DocKey` worked out from them), `sections.ts` (what each sidebar section holds),
  `addresses.ts` (`?doc=` both ways) and `DocDialog.tsx` (this dialog). Named `library`
  because the repository root already has `docs/` for the markdown.
- `components/`: presentation only, one `.module.css` per component. `FilterBar`,
  `InventoryGrid`, `VehicleCard`, `VehicleDetail`, `BidPanel`, the badge trio
  (`ConditionBadge`, `TitleStatusBadge`, `ReserveBadge`), `AuctionCountdown`,
  `VehicleImage` (graceful fallback).
- `hooks/`: React-aware orchestration. `useBids` (relays bid actions to the API,
  mirrors the bid map) and `useNow` (the one shared clock every countdown ticks on).
- `lib/`: pure, framework-free modules with their unit tests beside them.
  - `types.ts`: the `Vehicle` wire shape, including the server-derived auction facts.
  - `data.ts`: the single API seam. Query building, the TTL response cache,
    request aborts, bid POSTs.
  - `inventory.ts`: filter and sort state, and its URL to GET-parameter serialization.
  - `auction.ts`: status recomputation from server-sent windows, reserve display.
  - `format.ts`: currency, odometer, countdown, and date formatting (one
    CURRENCY/LOCALE constant).
- `styles/colors.css`, `sizes.css`, `typography.css`, `effects.css`: every
  design token, in the file named for what it controls. The Urban slate palette
  lives in `colors.css` (ADR-016): a light gray ground, brown-gray
  text, a teal accent with dark green and gold (ADR-016, addendum), every text and ground pair measured against WCAG
  AA by a unit test (`colors.test.ts`). A reskin is one file.
- `tests/e2e/` (repo root): Playwright smokes that prove the whole stack end to end.

### Its architecture

The backend is an onion because the rules live there; the frontend keeps the onion's
one load-bearing idea, imports only point inward, without the ceremony, because its
core intentionally moved server-side. Three rings, enforced by a single convention:

```
components/   outer ring: presentation only, consumes hooks and lib
    ▼
hooks/        middle ring: React-aware orchestration (useBids, useNow)
    ▼
lib/          inner ring: pure functions and the API seam, imports NO React,
              so it unit-tests in Node with no rendering and no mocks
```

`app/App.tsx` is the composition root, the same role `Program.cs` plays on the API side,
wiring the rings together; the view state it used to hold is in `app/hooks/`. And `lib/data.ts` is a genuine port:
when data moved from a JSON import to an API to a paged API, every change landed in
that one file.

## Beside the projects: samples/

`samples/maplarge` is The Shed, a sample that is not one of the eleven pieces: its own
solution (`TestProject.sln`, net10.0, controllers, a page in TypeScript compiled by tsc alone), its own tests,
its own twelve records served from its own running app, and its own deploy to a third web app
on the plan the two sites share. It was built for MapLarge's developer test project in
September 2026 on the starter they sent, in this project's working method: dependencies
inward in four folders instead of five projects, sealed records on the wire, a problem document
on every failure, a rules table beside the tests that hold it, and the palette carried over.
Nothing in it is referenced by TheYard and nothing in TheYard is referenced by it; the
repository's own tests skip the folder, because the sample holds its own copies of the rules.
Live at [theshed.stevenstout.biz](https://theshed.stevenstout.biz); start at
[`samples/maplarge/README.md`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/README.md).

## Where to start reading

- [`docs/ARCHITECTURE.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ARCHITECTURE.md): the layers, the rules that keep them, and where a change goes (served as Architecture overview under App Architecture).
- [`docs/STYLE.md`](https://github.com/SteveStout/TheYard/blob/main/docs/STYLE.md): naming, layering and commenting rules, with `.editorconfig` enforcing the mechanical half.
- ADR: Program.cs, explained and ADR: The React configuration, explained walk the two entry points line by line; ADR: The tests, explained walks the three suites.
