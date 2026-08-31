# Data Flow

How a vehicle travels from a JSON file on disk to a card in the browser, and how a bid
travels back. One rule shapes everything: **derive, don't store** — windows, statuses,
galleries, and the 100k inventory are all computed from stable ids, never persisted.

In the diagram, `Domain/…`, `Application/…`, `Infrastructure/…`, and `Api/…` are short
for `api/TheBlock.Domain/…`, `api/TheBlock.Application/…`, and so on.

```
 READ PATH
 ─────────
 data/vehicles.json (200 seeds, untouched)
     │
     ▼
 JsonFileVehicleSource ............... Infrastructure/JsonFileSources.cs
     │
     ▼
 SyntheticVehicleSource ×500 → 100,000  Infrastructure/SyntheticVehicleSource.cs
     │                                  (FNV-1a variants: Domain/Fnv1a.cs)
     ▼
 InventoryService ................... Application/InventoryService.cs
     │  galleries applied at startup    (picks: Domain/PhotoGallery.cs,
     │                                   pools: Api/photo-manifest.json)
     ▼
 HTTP endpoints ..................... Api/Program.cs   ◄── GET /api/vehicles?make=…&anchor_ms=…
     │  params → filter/sort/clock      Api/VehicleQueryParams.cs, Api/Clocks.cs
     ▼
 Search pipeline (in InventoryService.Search)
     │  1. bids overlaid .............. Application/BidService.cs
     │  2. Where(filter.Matches) ...... Domain/VehicleFilter.cs
     │  3. OrderBy(rank) .............. Domain/VehicleOrdering.cs
     │  4. Skip/Take (paging)           (windows: Domain/AuctionSchedule.cs)
     ▼
 VehicleWire ........................ Api/VehicleWire.cs
     │  + auction_starts_at/ends_at, auction_status, min_next_bid
     │
     └──── { total, vehicles } ────►  data.ts fetch seam ... src/lib/data.ts
                                          │  debounce 500 ms · TTL cache · abort
                                          ▼
                                      App state + URL sync .. src/App.tsx
                                          │
                                          ▼
                                      Cards / Detail / BidPanel  src/components/*
                                      format + countdown only   (src/lib/format.ts,
                                                                 src/lib/auction.ts)

 WRITE PATH (bids)
 ─────────────────
 BidPanel ........................... src/components/BidPanel.tsx
     │  POST /api/vehicles/{id}/bids { amount, anchor_ms }  via src/lib/data.ts
     ▼
 HTTP endpoint ...................... Api/Program.cs
     │
     ▼
 BidRules: validate → accept/win/reject  Domain/BidRules.cs
     │
     ▼
 BidService (in-memory map) ......... Application/BidService.cs
     │  response: updated vehicle + fresh min_next_bid
     ▼
 useBids: clear cache, refetch ...... src/hooks/useBids.ts
```

Every box in the diagram is a file; each step below names its path.

**All HTTP endpoints live in one place: `api/TheBlock.Api/Program.cs`.** It's the
composition root — it wires the dependency graph (sources → services), then declares
every route and hands straight off to the Application layer:

| Route | Handled by |
| --- | --- |
| `GET /api/vehicles` (filter/sort/page params) | `InventoryService.Search` |
| `GET /api/vehicles/{id}` | `InventoryService.GetById` |
| `GET /api/facets` | `InventoryService.Facets` |
| `POST /api/vehicles/{id}/bids` | `BidService.PlaceBid` → `BidRules` |
| `POST /api/vehicles/{id}/buy-now` | `BidService.BuyNow` → `BidRules` |
| `GET` / `DELETE /api/bids` | `BidService.Snapshot` / `Reset` |
| `GET /api/docs/readme` · `/dataflow` · `/resume` | files on disk |
| `GET /api/images/{file}` | static files (day-long `Cache-Control`) |

## The read path

1. **Seed** — `data/vehicles.json` (the challenge's 200 records, untouched) is read once
   at startup by `JsonFileVehicleSource` —
   `api/TheBlock.Infrastructure/JsonFileSources.cs` — deserializing into the `Vehicle`
   record, which lives with the other pure data shapes in `api/TheBlock.Data/`.
2. **Scale** — `SyntheticVehicleSource` —
   `api/TheBlock.Infrastructure/SyntheticVehicleSource.cs` — expands it to 100,000
   deterministic variants: each new id is hashed (FNV-1a,
   `api/TheBlock.Domain/Fnv1a.cs`) to vary VIN, year, odometer, prices, and bid state
   while inheriting the seed's make/model/trim mix.
3. **Enrich & cache** — `InventoryService` —
   `api/TheBlock.Application/InventoryService.cs` — applies each vehicle's photo gallery
   (hash-picked by `api/TheBlock.Domain/PhotoGallery.cs` from
   `api/TheBlock.Api/photo-manifest.json` pools, preferring the vehicle's own make) and
   materializes the list plus an id index, once, eagerly at startup.
4. **Query** — a request like `GET /api/vehicles?make=Ford&status=live&sort=price-asc`
   binds through `api/TheBlock.Api/VehicleQueryParams.cs`, which validates and produces
   a filter, a sort, and a clock (`api/TheBlock.Api/Clocks.cs`) — the client sends its
   local midnight (`anchor_ms`) so schedule math
   (`api/TheBlock.Domain/AuctionSchedule.cs`, `AuctionClock.cs`) agrees with the browser
   in any timezone. The buyer's bids overlay **before** filtering
   (`api/TheBlock.Application/BidService.cs`), so price bounds see the same figures the
   UI shows. Then `Where` → `OrderBy` → `Skip/Take`, all in memory:
   `api/TheBlock.Domain/VehicleFilter.cs` and `VehicleOrdering.cs`, applied in
   `InventoryService.Search`.
5. **Wire** — `api/TheBlock.Api/VehicleWire.cs` stamps the server-derived auction facts
   onto each vehicle — `auction_starts_at`, `auction_ends_at`, `auction_status`,
   `min_next_bid` — and the endpoint (`api/TheBlock.Api/Program.cs`) responds with a
   snake_case envelope `{ total, vehicles }`.
6. **Fetch** — `src/lib/data.ts` is the browser's single seam: it debounces filter
   changes (500 ms), caches responses per query string (5-minute TTL; hits skip the
   debounce), and aborts superseded requests. The query string itself is built by
   `src/lib/inventory.ts` — the same serializer that feeds the address bar.
7. **Render** — `src/App.tsx` holds the page and mirrors filters plus `?vehicle={id}`
   into the URL; components (`src/components/`) format currency
   (`src/lib/format.ts`), tick countdowns from the server's window, and recompute
   live/ended locally as time passes (`src/lib/auction.ts`). No business math runs in
   the browser.

## The write path (bids)

1. `src/components/BidPanel.tsx` posts `{ amount, anchor_ms }` to
   `POST /api/vehicles/{id}/bids` via `src/lib/data.ts`.
2. `api/TheBlock.Domain/BidRules.cs` is the sole authority: live-window check, tiered
   minimum increment, and the buy-now override (a bid at or above `buy_now_price` wins
   outright at that price).
3. Accepted or won bids land in the in-memory map in
   `api/TheBlock.Application/BidService.cs` (single anonymous buyer — isolated demo);
   rejections return 400 with a human-readable reason the panel shows.
4. The response carries the updated vehicle (fresh `min_next_bid` included); the client
   (`src/hooks/useBids.ts`) clears the query cache and refetches, so lists, filters, and
   totals all reflect the new bid — because the overlay in read-step 4 feeds the same
   pipeline every read uses.
