# Projects

Seven pieces, listed inside-out — each may only depend on the ones above it.

## TheBlock.Data

The innermost ring and the language every other layer speaks: the pure data records —
`Vehicle` exactly as it appears in `data/vehicles.json`, and `PhotoEntry` from the photo
manifest. Sealed records, value equality, zero dependencies, zero behavior. If it
computes anything, it doesn't belong here.

## TheBlock.Domain

The business rules, as pure functions over Data: `AuctionSchedule` derives each
vehicle's auction window from its id, `BidRules` owns increments, validation, and the
buy-now override, `VehicleFilter` is the search predicate, `VehicleOrdering` ranks
results, and `PhotoGallery` picks deterministic galleries. Everything takes its clock as
an argument (`AuctionClock`), so every rule is testable with a fixed timestamp and no
mocking.

## TheBlock.Application

The use cases, and the seams. `InventoryService` loads the dataset once and answers
search/facet/by-id queries by composing Domain rules; `BidService` holds the buyer's
in-memory bid state and applies it *before* filtering so prices never disagree with the
UI. Both consume data through ports (`IVehicleSource`, `IPhotoManifestSource`) — the
interfaces that make Infrastructure swappable and the tests trivial to fake.

## TheBlock.Infrastructure

The adapters behind those ports: `JsonFileVehicleSource` and
`JsonFilePhotoManifestSource` deserialize the files on disk, and
`SyntheticVehicleSource` decorates a source to expand 200 seeds into 100,000
deterministic records — proof the port design works, since nothing above it changed when
the dataset grew 500×.

## TheBlock.Api

The composition root and nothing more: `Program.cs` wires the dependency graph and
declares every HTTP route, `VehicleQueryParams` binds and validates GET parameters,
`Clocks` resolves the client's midnight anchor, and `VehicleWire` stamps server-derived
auction facts onto each outgoing vehicle. Endpoints contain no logic — only binding and
delegation.

## TheBlock.Tests

One suite per ring: Domain rules with fixed clocks, Application services with in-memory
fakes at the ports, Infrastructure against both fixtures and the real dataset, and
integration tests that boot the actual host in-memory (`WebApplicationFactory`) to
verify routes, parameters, error paths, and the full bid lifecycle — 81 tests, no
running server required.

## Frontend (src/)

React + TypeScript, deliberately thin — no business math runs in the browser:

- `main.tsx` — entry point: mounts `App` and imports the design tokens once.
- `App.tsx` — composition root: view state, the debounced fetch effect, URL sync
  (filters + `?vehicle={id}`), browser history, and Load More.
- `components/` — presentation only, one `.module.css` per component: `FilterBar`,
  `InventoryGrid`, `VehicleCard`, `VehicleDetail`, `BidPanel`, the badge trio
  (`ConditionBadge`, `TitleStatusBadge`, `ReserveBadge`), `AuctionCountdown`,
  `VehicleImage` (graceful fallback), and `DocsMenu` (this dialog).
- `hooks/` — React-aware orchestration: `useBids` (relays bid actions to the API,
  mirrors the bid map) and `useNow` (the one shared clock every countdown ticks on).
- `lib/` — pure, framework-free modules with their unit tests beside them:
  - `types.ts` — the `Vehicle` wire shape, including the server-derived auction facts.
  - `data.ts` — the single API seam: query building, the TTL response cache,
    request aborts, bid POSTs.
  - `inventory.ts` — filter/sort state and its URL ↔ GET-parameter serialization.
  - `auction.ts` — status recomputation from server-sent windows, reserve display.
  - `format.ts` — currency, odometer, countdown, and date formatting (one
    CURRENCY/LOCALE constant).
- `styles/tokens.css` — every color, space, radius, type, and shadow token; the
  OPENLANE-inspired theme lives here, so a reskin is one file.
- `tests/e2e/` (repo root) — Playwright smokes that prove the whole stack end to end.

### Its architecture

The backend is an onion because the rules live there; the frontend keeps the onion's
one load-bearing idea — imports only point inward — without the ceremony, because its
core intentionally moved server-side. Three rings, enforced by a single convention:

```
components/   outer ring — presentation only; consumes hooks and lib
    ▼
hooks/        middle ring — React-aware orchestration (useBids, useNow)
    ▼
lib/          inner ring — pure functions and the API seam; imports NO React,
              so it unit-tests in Node with no rendering and no mocks
```

`App.tsx` is the composition root — the same role `Program.cs` plays on the API side —
holding view state and wiring the rings together. And `lib/data.ts` is a genuine port:
when data moved from a JSON import to an API to a paged API, every change landed in
that one file.
