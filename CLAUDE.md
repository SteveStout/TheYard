# TheYard

A used-vehicle auction platform. React + TypeScript (Vite) frontend, .NET minimal API
backend in onion architecture.

This line said "an industrial and farm equipment auction marketplace" until 2026-09-03.
That rename was considered on the first day and deliberately not done, and the file that
tells an agent what it is working on was left describing the version that never happened,
which is the worst place in a repository for a sentence to be wrong.

## Architecture, and it is not negotiable

Nine projects; among the five that form the onion, dependencies point INWARD only:

- `api/TheYard.Data` - pure records, ZERO dependencies, ZERO behavior. If it computes anything it does
  not belong here.
- `api/TheYard.Domain` - the rules. Depends only on Data.
- `api/TheYard.Application` - use cases behind three ports (`IVehicleSource`, `IPhotoManifestSource`,
  `IBidStore`). Depends on Domain.
- `api/TheYard.Infrastructure` - the relational adapters: EF Core over Azure SQL Database or SQLite, the
  JSON seed readers, the synthetic scale-up decorator, the Identity user entity. Implements the ports.
- `api/TheYard.Api` - host and endpoints. Composition root. NO business logic in endpoints.

Beside the onion: `api/TheYard.Infrastructure.Cosmos` (the same ports and an Identity user store over
Azure Cosmos DB on the SDK, referencing Infrastructure for the shared user entity),
`api/TheYard.Database` (the SQL Server schema as a DACPAC, the authority for the relational schema),
`api/TheYard.Migrations.Sqlite` (the SQLite schema's history), `api/TheYard.Experiment` (a console tool
for the partition key experiment), and `api/TheYard.Tests`. One process runs BOTH stores side by side and
picks one per request from the `X-Yard-Store` header or the container's default (ADR-066); the two
container groups default to different stores and each is one site.

Frontend keeps the same discipline: `components` -> `hooks` -> `lib`. **`src/lib` imports nothing from
React.**

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
  (`BidService.IsSold`) supplies it, and the wire says `sold` on every vehicle. A listing or a rule
  that forgets it recreates the second buyer (ADR: Accounts and per-user bids, addendum).

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
