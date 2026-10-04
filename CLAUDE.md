# TheYard

A used-vehicle auction platform. React + TypeScript (Vite) frontend, .NET minimal API
backend in onion architecture.

This line said "an industrial and farm equipment auction marketplace" until 2026-09-03.
That rename was considered on the first day and deliberately not done, and the file that
tells an agent what it is working on was left describing the version that never happened,
which is the worst place in a repository for a sentence to be wrong.

## Before you change anything

Read the record that governs it. The decisions are `docs/ADR-*.md`, served from the running app under
Decision Records, and the ones a test enforces are listed beside their test in
`docs/ADR-075-the-rules-a-change-has-to-pass.md`. A change that contradicts a record changes the record
first, as an addendum that says when it stopped being true rather than an edit that makes it look like
it was always this way. The test goes in the same commit as the change, and the one gate runs
green before anything rolls (ADR-068). Any build warning is red.

## Architecture, and it is not negotiable

Ten projects; among the five that form the onion, dependencies point INWARD only, and
`api/TheYard.Tests/OnionTests.cs` fails the build when one points outward (ADR: Onion and SOLID, how
this codebase holds them):

- `api/TheYard.Data` - pure records, ZERO dependencies, ZERO behavior. If it computes anything it does
  not belong here.
- `api/TheYard.Domain` - the rules. Depends only on Data.
- `api/TheYard.Application` - use cases and ports. `Auction` is what the endpoints ask about the auction; the
  auction's three ports are `IVehicleSource`, `IPhotoManifestSource` and `IBidStore`, beside the
  operator's ports (`IActivityStore`, `ILogStore`, `IStoreExperiment` and the rest). Depends on Domain.
- `api/TheYard.Infrastructure` - the relational adapters: EF Core over Azure SQL Database or SQLite, the
  JSON seed readers, the synthetic scale-up decorator, the Identity user entity. Implements the ports.
- `api/TheYard.Api` - host and endpoints. Handlers in `Endpoints/`, the composition root in
  `Composition/`, and `Program.cs` a table of contents for both. NO business logic in endpoints.

Beside the five: `api/TheYard.Infrastructure.Cosmos` (the second adapter in Infrastructure's ring: the
same ports and an Identity user store over Azure Cosmos DB on the SDK, referencing Infrastructure only
for the shared user entity),
`api/TheYard.Database` (the SQL Server schema as a DACPAC, the authority for the relational schema),
`api/TheYard.Migrations.Sqlite` (the SQLite schema's history), `api/TheYard.Experiment` (a console tool
for the partition key experiment), and `api/TheYard.Tests`. One process runs BOTH stores side by side and
picks one per request from the `X-Yard-Store` header or the container's default (ADR-066); the two
container groups default to different stores and each is one site.

Frontend keeps the same discipline: `components` -> `hooks` -> `lib`. **`src/lib` imports nothing from
React.** Every component has a folder of its own, `src/components/<section>/<Name>/`, holding its `.tsx`,
its `.module.css` and an `index.ts` that re-exports it, so an import reads `components/<section>/<Name>`; a
component another section renders lives in `shared/`, and `src/lib` and `src/hooks` stay where they are
(ADR: One folder per component). `src/app` is the shell: `App.tsx` reads like a table of contents, and what
the app knows is one hook per file in `src/app/hooks`, named for what it gives back. `src/library` is the
documents the site serves, as data, and the window one opens in. **Every file in those two folders opens with
a Does / Does not / Used by header, one job per file, 300 lines at most**; `FileHeaderTests` holds all three
and checks "Used by" against the real importers (ADR: The React configuration, explained, the addendum on the
split).

## Rules that must survive any change

- **Derive, do not store.** Auction windows derive from the item id via FNV-1a hash. Status derives from
  the window and the clock. Nothing schedule-related is persisted.
- **The server owns every derived fact, and the clock.** The server computes windows, status and
  `min_next_bid` on its own clock: now, and the UTC midnight that began the day, one anchor for every
  visitor (`AuctionClock.Utc`). No request names a day; `anchor_ms` left the API in 1.0.0.112 and is
  ignored if an old page sends it. **Never re-implement auction math in TypeScript.** This rule exists
  because an earlier version derived it on both sides and drifted on a daylight-saving transition, and a
  later one let the caller choose the anchor (ADR: Three readers with no memory of the project).
- **The wire is snake_case** and matches the dataset exactly. No mapping layer.
- **Empty is valid, null is the error.** Never return null for a collection.
- **Money is whole dollars as `int`**, because the dataset's prices are whole dollars and every rule adds
  whole-dollar increments; nothing needs cents. **Every derived or recorded instant is milliseconds since
  the epoch as `long`, UTC** (the clock's anchor, the window's start and end, a bid's `at_ms`), the same unit the
  browser's clock uses; the dataset's own `auction_start` date string passes through as the dataset has it
  and nothing is derived from it.
- Bid rules live only in `TheYard.Domain/BidRules.cs`. A bid at or above buy-now wins AT the buy-now
  price, and that check runs BEFORE the increment check. A vehicle anybody has bought is SOLD, to
  everybody: that check runs before all the others, the caller that holds everybody's standing
  (`BidService.IsSold`, asked through `Auction.IsSold`) supplies it, and the wire says `sold` on every
  vehicle. A listing or a rule that forgets it recreates the second buyer (ADR: Accounts and per-user
  bids, addendum). Raising a vehicle's shown price over a bid, and whether the reserve is met, live only
  in `TheYard.Domain/StandingRules.cs` (`RaisedTo`, `ReserveOf`).

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

## Environment

Windows. **`vite.config.ts` excludes `**/api/**` from the watcher because dotnet holds locks on `obj/`
and Vite crashes with EBUSY without it. Do not remove that exclusion.**
