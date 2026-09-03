# Data Flow

How a vehicle travels from a JSON file on disk to a card in the browser, and how a bid
travels back. One rule shapes everything: **derive, don't store**. Windows, statuses,
galleries, and the 100k inventory are all computed from stable ids, never persisted.

[![TheYard data flow: the read path from the seed file to the cards, top to bottom, and the write path of a bid beside it, every box a file](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/dataflow.png)](https://theyard.stevenstout.biz/api/docs/diagrams/dataflow)

*A preview. [Open the data flow diagram in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/dataflow)
to zoom in and follow it; every diagram on this site opens that way (ADR: Diagram pages).
The source is [`docs/images/dataflow.svg`](https://github.com/SteveStout/TheYard/blob/main/docs/images/dataflow.svg).*

Every box in the diagram is a file; each step below names its path.

**All HTTP endpoints live in one place: `api/TheYard.Api/Program.cs`.** It is the
composition root: it wires the dependency graph (sources, then services), then declares
every route and hands straight off to the Application layer. ADR: Program.cs, explained
walks that file top to bottom.

| Route | Handled by |
| --- | --- |
| `GET /api/vehicles` (filter/sort/page params) | `InventoryService.Search` |
| `GET /api/vehicles/{id}` | `InventoryService.GetById` |
| `GET /api/facets` | `InventoryService.Facets` |
| `POST /api/vehicles/{id}/bids` | `BidService.PlaceBid` → `BidRules` |
| `POST /api/vehicles/{id}/buy-now` | `BidService.BuyNow` → `BidRules` |
| `GET` / `DELETE /api/bids` | `BidService.Snapshot` / `Reset` |
| `GET /api/docs/{slug}` · `/api/docs/diagrams/{name}` | the documents catalog and the diagram pages (ADR-017, ADR-020) |
| `GET /api/docs/bicep` · `/api/docs/resume` | files on disk |
| `GET /api/images/{file}` | static files (day-long `Cache-Control`) |
| `GET /api/version` | build provenance (ADR-005) |
| `GET /healthz` · `/readyz` | liveness and readiness |
| `GET /api/health` · `/api/errors` · `/api/admin/azure` | the Admin tab (ADR-010) |

## The read path

1. **Seed.** `data/vehicles.json` (the challenge's 200 records, untouched) is read once
   at startup by `JsonFileVehicleSource` (`api/TheYard.Infrastructure/JsonFileSources.cs`),
   deserializing into the `Vehicle` record, which lives with the other pure data shapes
   in `api/TheYard.Data/`.
2. **Scale.** `SyntheticVehicleSource` (`api/TheYard.Infrastructure/SyntheticVehicleSource.cs`)
   expands it to 100,000 deterministic variants: each new id is hashed (FNV-1a,
   `api/TheYard.Domain/Fnv1a.cs`) to vary VIN, year, odometer, prices, and bid state
   while inheriting the seed's make/model/trim mix.
3. **Enrich and cache.** `InventoryService` (`api/TheYard.Application/InventoryService.cs`)
   applies each vehicle's photo gallery (hash-picked by `api/TheYard.Domain/PhotoGallery.cs`
   from `api/TheYard.Api/photo-manifest.json` pools, preferring the vehicle's own make)
   and materializes the list plus an id index, once, eagerly at startup.
4. **Query.** A request like `GET /api/vehicles?make=Ford&status=live&sort=price-asc`
   binds through `api/TheYard.Api/VehicleQueryParams.cs`, which validates and produces
   a filter, a sort, and a clock (`api/TheYard.Api/Clocks.cs`). The client sends its
   local midnight (`anchor_ms`) so schedule math
   (`api/TheYard.Domain/AuctionSchedule.cs`, `AuctionClock.cs`) agrees with the browser
   in any timezone. The buyer's bids overlay **before** filtering
   (`api/TheYard.Application/BidService.cs`), so price bounds see the same figures the
   UI shows. Then `Where` → `OrderBy` → `Skip/Take`, all in memory:
   `api/TheYard.Domain/VehicleFilter.cs` and `VehicleOrdering.cs`, applied in
   `InventoryService.Search`.
5. **Wire.** `api/TheYard.Api/VehicleWire.cs` stamps the server-derived auction facts
   onto each vehicle (`auction_starts_at`, `auction_ends_at`, `auction_status`,
   `min_next_bid`), and the endpoint (`api/TheYard.Api/Program.cs`) responds with a
   snake_case envelope `{ total, vehicles }`.
6. **Fetch.** `src/lib/data.ts` is the browser's single seam: it debounces filter
   changes (500 ms), caches responses per query string (5-minute TTL; hits skip the
   debounce), and aborts superseded requests. The query string itself is built by
   `src/lib/inventory.ts`, the same serializer that feeds the address bar.
7. **Render.** `src/App.tsx` holds the page and mirrors filters plus `?vehicle={id}`
   into the URL; components (`src/components/`) format currency
   (`src/lib/format.ts`), tick countdowns from the server's window, and recompute
   live/ended locally as time passes (`src/lib/auction.ts`). No business math runs in
   the browser.

## The write path (bids)

1. `src/components/BidPanel.tsx` posts `{ amount, anchor_ms }` to
   `POST /api/vehicles/{id}/bids` via `src/lib/data.ts`.
2. `api/TheYard.Domain/BidRules.cs` is the sole authority: live-window check, tiered
   minimum increment, and the buy-now override (a bid at or above `buy_now_price` wins
   outright at that price).
3. Accepted or won bids land in the in-memory map in
   `api/TheYard.Application/BidService.cs` (single anonymous buyer, an isolated demo);
   rejections return 400 with a human-readable reason the panel shows.
4. The response carries the updated vehicle (fresh `min_next_bid` included); the client
   (`src/hooks/useBids.ts`) clears the query cache and refetches, so lists, filters, and
   totals all reflect the new bid, because the overlay in read-step 4 feeds the same
   pipeline every read uses.
