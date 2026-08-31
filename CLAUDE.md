# TheYard

An industrial and farm equipment auction marketplace. React + TypeScript (Vite) frontend,
.NET minimal API backend in onion architecture.

## Architecture, and it is not negotiable

Five projects, dependencies point INWARD only:

- `api/TheYard.Data` - pure records, ZERO dependencies, ZERO behavior. If it computes anything it does
  not belong here.
- `api/TheYard.Domain` - the rules. Depends only on Data.
- `api/TheYard.Application` - use cases behind ports (interfaces). Depends on Domain.
- `api/TheYard.Infrastructure` - adapters: file loading, the synthetic scale-up. Implements the ports.
- `api/TheYard.Api` - host and endpoints. Composition root. NO business logic in endpoints.

Frontend keeps the same discipline: `components` -> `hooks` -> `lib`. **`src/lib` imports nothing from
React.**

## Rules that must survive any change

- **Derive, do not store.** Auction windows derive from the item id via FNV-1a hash. Status derives from
  the window and the clock. Nothing schedule-related is persisted.
- **The server owns every derived fact.** The client sends `anchor_ms`, its own local midnight, and the
  server computes windows, status and `min_next_bid`. **Never re-implement auction math in TypeScript.**
  This rule exists because an earlier version derived it on both sides and drifted on a daylight-saving
  transition.
- **The wire is snake_case** and matches the dataset exactly. No mapping layer.
- **Empty is valid, null is the error.** Never return null for a collection.
- **decimal for money. DateTimeOffset for time, stored UTC.**
- Bid rules live only in `TheYard.Domain/BidRules.cs`. A bid at or above buy-now wins AT the buy-now
  price, and that check runs BEFORE the increment check.

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

## Environment

Windows. **`vite.config.ts` excludes `**/api/**` from the watcher because dotnet holds locks on `obj/`
and Vite crashes with EBUSY without it. Do not remove that exclusion.**