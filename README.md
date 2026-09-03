# TheYard, a used-vehicle auction platform

**Live:** [theyard.stevenstout.biz](https://theyard.stevenstout.biz)

TheYard is my portfolio implementation of a used-vehicle auction platform: browse a large
inventory, inspect a vehicle in detail, and place bids against a simulated room of other
bidders. The frontend is a React app backed by a .NET 10 API that owns the data, the
search, and the auction rules, storing accounts and bids in Azure SQL Database reached
with a managed identity, so the connection string in the container is a server name and
an authentication mode and nothing worth stealing.

It runs on a free tier and costs nothing, and it keeps serving when the database does
not: the catalogue falls back to files and the health endpoint says which store answered.
The Admin tab shows the running system reporting on itself, including every SQL statement
it has sent and how long the database took.

![The Yard inventory on a laptop: the docked sidebar of documents and decision records beside the vehicle grid](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-home.jpg)

Everything about how it is built and hosted is served from inside the running app, under
App Architecture, Hosting, CI/CD and Best Practices in the sidebar. Fifty-one decision
records explain each choice, and the code samples in them are read from the running build
rather than pasted, so a record cannot drift from the code it describes. The shape of it:

[![TheYard infrastructure: the request path, the deploy path, and the designed production target](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/infrastructure.png)](https://theyard.stevenstout.biz/api/docs/diagrams/infrastructure)

*A preview. [Open the infrastructure diagram in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/infrastructure) to zoom in and follow it. The data flow has [its own drawing](https://theyard.stevenstout.biz/api/docs/diagrams/dataflow) too.*

## How to Run

Requires [Node 20+](https://nodejs.org) (built on Node 24) and the
[.NET 10 SDK](https://dotnet.microsoft.com/download). On Windows:
`winget install OpenJS.NodeJS.LTS Microsoft.DotNet.SDK.10`.

```
npm install
npm start          # API + frontend in one command; opens the browser
```

(Or separately: `npm run api` and `npm run dev` in two terminals.)

Open http://localhost:5173. The dev server proxies `/api` to the .NET API, which serves
the inventory and the vehicle photos (`/api/images/...`). The inventory is **100,000
records**, deterministically synthesized at startup from the 200-record seed dataset
(`Inventory:TargetCount` in `api/TheYard.Api/appsettings.json`), so there is no giant
file in the repo. All filtering, sorting, and paging are server-side via LINQ over GET
parameters; the landing page is the top 100 by auction time (live, ending soonest first):

```
GET /api/vehicles?make=Ford&status=live&sort=price-asc&limit=100
```

Parameters: `q` (matches every filterable field, including derived auction status),
`make`, `body_style`, `title_status`, `province`, `status` (+ `anchor_ms`),
`min_condition`, `price_min`, `price_max`, `sort` (ending-soonest, price-asc,
price-desc, condition, most-bids), `limit` (default 100, max 500), `offset`. Responses
are an envelope `{ total, vehicles }`, each vehicle carrying server-derived auction
facts (`auction_starts_at`, `auction_ends_at`, `auction_status`, `min_next_bid`).
Invalid `status`, `sort` or `anchor_ms` values return 400 as RFC 9457 ProblemDetails
with the message in `detail`. `GET /api/vehicles/{id}` fetches one vehicle;
`GET /api/facets` feeds the filter dropdowns from the full dataset.

Bidding is server-side and validated by the domain rules:
`POST /api/vehicles/{id}/bids` `{ amount, anchor_ms }` answers accepted or won, or 400
in the same problem shape; `POST /api/vehicles/{id}/buy-now`; `GET /api/bids` (the
single anonymous buyer's standing); `DELETE /api/bids` (reset). Bid state lives in API
memory and is overlaid on vehicles before filtering, so price filters see what the UI
shows. If the API is not running, the app shows a clear error state with a retry.

The app also serves its own documentation and health:
`GET /api/docs/{slug}` (every document in the sidebar, live code samples expanded at
request time), `GET /api/docs/diagrams/{name}` (a diagram on its own zoomable page),
`GET /api/version` (the build and commit the footer shows), `GET /healthz` and
`GET /readyz` (liveness and readiness), `GET /api/health`, `GET /api/errors` and
`GET /api/admin/azure` (the Admin tab).

Other scripts:

```
npm test           # frontend unit tests (Vitest)
npm run test:api   # API unit + integration tests (xUnit)
npm run test:e2e   # end-to-end smokes (Playwright; starts both servers itself)
npm run build      # typecheck + production bundle to dist/
npm run preview    # serve the production build
```

CI (GitHub Actions, `.github/workflows/ci.yml`) runs all three suites on every push, and
a green run on `main` builds the image and rolls the live container with no human step
(`.github/workflows/deploy.yml`).

The .NET suite is measured as well as run, and published as an annotation on every run so
it can be read without a GitHub sign-in. At 1.0.0.65 it was **89.6% of lines and 71.7% of
branches**; the current figure is on the latest run rather than in this paragraph. The
shape matters more than the total, and it is the shape the architecture predicts:

| Project | Lines | Branches |
| --- | ---: | ---: |
| `TheYard.Data` | 100.0% | 100.0% |
| `TheYard.Domain` | 98.8% | 97.9% |
| `TheYard.Migrations.Sqlite` | 97.6% | 100.0% |
| `TheYard.Infrastructure` | 93.8% | 89.3% |
| `TheYard.Application` | 93.2% | 88.8% |
| `TheYard.Api` | 78.2% | 64.1% |

The rules and the use cases are the parts worth being sure about. The host is lowest
because two of its classes talk to Azure with a managed identity, and CI has no Azure
credential and is never getting one; what is worth asserting about those two is that they
degrade rather than throw when the identity endpoint is not there, and that is tested
(ADR: Counting what the tests cover).

To refresh the photo set from Wikimedia Commons, run `node scripts/fetch_photos.mjs`.

## How It Was Built

Built domain-first, with tests before any UI. It then grew in deliberate passes into a
demonstration of how I build production systems: the .NET API in onion architecture,
server-side filtering, sorting and paging over a 100,000-record synthetic dataset,
server-owned bidding rules, three test suites, CI, a container, a live host, and a
written architecture the code is reviewed against.

The work was pair-built with Claude Code throughout. I directed the scope, the
architecture, and every product decision, and I am happy to walk through the reasoning
behind any line of it.

## Workflow

AI-assisted, verification-driven. I directed scope, architecture, and product decisions;
Claude Code implemented under that direction, and nothing merged on trust: every change
ran the typechecker and all three suites, UI work was verified against real screenshots
at desktop, tablet and mobile widths, and features were driven end to end in a headless
browser before being called done. The build went domain-first (rules and tests before any
UI), then grew in deliberate passes: frontend, API, scale, bidding, hosting, then the
documentation and observability passes. Two adversarial reviews ran mid-stream, one
multi-agent and one staff-level, and their findings were fixed, tested, and in one case
turned into a regression test. The living documentation is served inside the app, so the
walkthrough can happen without leaving it.

On the second build day the loop tightened further: a lead session wrote the tasks and a
developer session implemented them, every change gated by the full suite before commit
and verified from the live domain after the deploy. Eighteen versions shipped that day,
each with its own changelog line and, where it decided something, its own record.

## Assumptions and Scope

- **`current_bid` is null for 112 of 200 vehicles** (the ones with `bid_count: 0`). The
  sample record shows a number, but the data is authoritative: the type is
  `number | null`. Before any bids exist, the minimum acceptable bid is the opening ask
  (no increment), a reserve cannot be met, and the UI labels the price "Starting bid".
- **Auction windows are derived, not read.** `auction_start` is synthetic, so each
  vehicle's id hashes to an end time spread across two days before to five days after
  "now" (anchored to local midnight), with a two to four day duration. Windows are stable
  across reloads within a day and re-seed at midnight, so the inventory always shows a
  live mix of ended, live, and upcoming auctions.
- **A bid at or above the Buy Now price wins immediately at the Buy Now price**, even if
  it would fail the minimum-increment check: the instant-win rule takes precedence.
- **Single anonymous buyer.** Your bids live in the API's memory, mark you high bidder,
  and survive browser reloads (not API restarts); there are no competing bidders
  advancing prices. "Reset bids" (in the sidebar, or the header on a phone) clears the
  slate.
- **Currency is CAD** (`en-CA`) since every listing is Canadian; one constant in
  `src/lib/format.ts` switches it.
- **Photos are representative, not the actual lot.** 50 free-license photos (10 per body
  style, modern generations) are fetched from Wikimedia Commons and mapped
  deterministically per vehicle id, preferring photos of the vehicle's own make. Real
  listings would use real lot photography; credits in
  `api/TheYard.Api/wwwroot/images/CREDITS.md`.
- **The API owns everything**: data, filtering, sorting, paging, photo mapping, auction
  scheduling, and bid validation. The browser formats, counts down, and relays actions.
- Out of scope by design: auth, accounts, seller tooling, checkout, payments, a database,
  real-time multi-user bidding.

## Stack

- **Frontend:** React 19 + TypeScript (strict) on Vite 8; plain CSS via CSS Modules over
  a single design-token sheet (`src/styles/tokens.css`); Vitest for tests. No component,
  icon, state or CSS libraries, and no router: icons are small inline SVGs and the
  address bar is the application state. Three runtime dependencies: react, react-dom, and
  marked for rendering the served documents. The palette is Figma's Urban slate, gray,
  brown and blue, with every text and ground pair measured against WCAG AA by a unit
  test, and Poppins from Google Fonts (the one external asset) with a system fallback.
- **Backend:** .NET 10 minimal API in onion architecture (`api/`): `TheYard.Data`
  (the pure data records, no dependencies), `TheYard.Domain` (photo selection, auction
  schedule, filter and bid rules), `TheYard.Application` (the `InventoryService` and
  `BidService` use cases behind source ports), `TheYard.Infrastructure` (the EF Core
  adapters over Azure SQL Database or SQLite, the JSON readers that seed them, the synthetic scale-up), `TheYard.Api` (host, endpoints, static images, the
  served documents, observability). Filtering is LINQ over GET parameters, including
  auction status; all auction math lives in Domain and travels on the wire, so the
  browser only formats. `src/lib/data.ts` is the frontend's single data seam.
- **Hosting:** a hand-authored multi-stage Dockerfile, an image in Azure Container
  Registry, a container group on Azure Container Instances, and Netlify's free tier as
  the TLS edge in front of it. GitHub Actions builds and rolls it on every green push.
  `infra/main.bicep` holds the production design (App Service behind Front Door with an
  origin lock), deliberately undeployed and explained on the Hosting page.
- **Database:** Azure SQL Database through EF Core, behind the same ports the JSON
  readers used to answer, with SQLite for local development and CI because neither has
  an Azure credential and neither should need one. There is no password anywhere: the
  server was created Entra-only, so it has no SQL login to have one, and the container
  authenticates as the managed identity it already carried. The schema is a SQL project
  of hand-written DDL that compiles to a DACPAC and is the authority; EF maps to it and a
  conformance test fails the build when the two disagree, and the running application
  holds read and write and cannot alter a table. The catalogue is read once into memory,
  so the database is not on the path a request takes, and a container that cannot reach
  it serves the catalogue from files and says so. ADR: The SQL Server backend, ADR: Data
  first, and ADR: Two providers, explained.

## What I Built

- **Inventory:** responsive card grid (3/2/1 across), token search over year, make,
  model, and trim, filters for make, body style, title status, province, auction status,
  minimum condition, and price range, all applied server-side (debounced GET requests),
  five server-side sorts (ending soonest with live first, price both ways, condition,
  most bids), Load More paging, and a clear empty state.
- **Detail view:** image gallery with thumbnails and graceful fallback art, full specs,
  condition grade with report and damage notes, a warning banner for salvage or rebuilt
  titles, seller and location, and the auction panel.
- **Bidding:** live countdowns on a shared clock, tiered minimum increments, validation
  with buyer-facing reasons, a persistent "You're the high bidder" state, Buy Now with a
  distinct sold and purchase-price presentation, and bids that survive refresh.
- **Accounts:** register or sign in with an address and a password, and the bid is
  yours. The session is a signed token in a cookie the page cannot read, the bids are
  keyed on the person as well as the vehicle, and the account view lists what you have
  bid on and whether you are still winning. Two visitors can now outbid each other and
  both be told the truth about it.
- **Navigation:** every view is a GET URL. Filters, sorts, the open vehicle and the Admin
  tab are all shareable, deep-linkable and browser-Back friendly, with no router.
- **A sidebar that documents the app from inside it:** App Architecture, Hosting, CI/CD,
  Best Practices, Changelog and About, holding the architecture and style pages, the
  data flow, infrastructure and entity relationship diagrams on their own zoomable
  pages, fifty-one decision records in one numbered index, the Bicep infrastructure, my resume, and
  How this was built, which says plainly that an AI agent wrote most of this and
  points at the evidence for judging what that produced.
- **An Admin tab:** timed health checks, the recent-errors list (server and browser
  alike), the container group's own state read from Azure with a managed identity, the
  last hour of traffic as Application Insights recorded it, and every SQL statement the
  application has sent, with the request that caused it, how long the database took, and
  its parameters listed by name and type. Not their values: the page is public, and the
  type it is built from has no field to put a value in (ADR: What the database is
  actually doing).

## Strengths

- **GET-parameter-driven filtering and navigation.** Every filter, the text search,
  sorting, and paging are query parameters on `GET /api/vehicles`, applied server-side
  with LINQ, and the browser's address bar mirrors the same parameters, so any filtered
  view is shareable and bookmarkable. Opening a vehicle is GET navigation too
  (`?vehicle={id}` pushes a history entry): the browser's Back button closes the detail,
  Forward reopens it, and a cold load of a vehicle URL deep-links straight to it.
  *Where:* `src/lib/inventory.ts` (URL and filter serialization), `src/App.tsx`
  (pushState and popstate), `api/TheYard.Api/VehicleQueryParams.cs` (binding),
  `api/TheYard.Domain/VehicleFilter.cs` (the LINQ predicate).
- **Debounced, cached requests.** Filter changes debounce 500 ms so typing does not
  hammer the API, and responses are cached per query string (5-minute TTL, bounded).
  Cache hits skip the debounce entirely: the delay only exists to protect the server,
  and a hit never touches it.
  *Where:* `src/lib/data.ts` (cache, `peekVehicles`), `src/App.tsx` (the debounced
  fetch effect), `api/TheYard.Api/Program.cs` (cache headers).
- **Server-side pagination at scale.** 100,000 records, but the wire only ever carries a
  page: an envelope of `{ total, vehicles }` with `limit` and `offset`, a landing page of
  the top 100 by auction time, and Load More to walk deeper.
  *Where:* `api/TheYard.Application/InventoryService.cs` (`Search`),
  `api/TheYard.Infrastructure/SyntheticVehicleSource.cs` (the 100k expansion),
  `src/App.tsx` (`loadMore`).
- **A search that does its work once.** Both halves of a free-text comparison are
  precomputed: each vehicle's searchable text when the dataset loads, each query's
  tokens when the filter compiles. The version before this rebuilt both inside the
  loop, so one search allocated a lowercase copy of nine fields a hundred thousand
  times for a query typed once. The scan went from a 37 ms median to 17 ms across the
  full dataset, measured by a test in the suite rather than asserted in prose. The
  auction status stays out of the index on purpose, because the clock decides it, and
  it is computed only for tokens the static text did not already satisfy.
  *Where:* `api/TheYard.Domain/VehicleSearchIndex.cs`, `VehicleFilter.cs`
  (`Compile`), `api/TheYard.Application/InventoryService.cs` (built with the
  dataset), `api/TheYard.Tests/SearchIndexBenchmarkTests.cs` (the measurement),
  `VehicleSearchIndexTests.cs` (the indexed and unindexed paths must agree).
- **One authoritative home for every business rule.** Auction windows, status, minimum
  increments, bid validation and buy-now precedence all live in `TheYard.Domain` and
  nowhere else. The wire carries the derived facts (`auction_ends_at`, `min_next_bid`)
  so the browser only formats and counts down. This was not free: early versions mirrored
  the math in TypeScript, and cross-language drift bit twice (a timezone anchor, then
  DST) before the consolidation. The architecture exists because the bug class it
  eliminates actually happened.
  *Where:* `api/TheYard.Domain/AuctionSchedule.cs`, `BidRules.cs`, and
  `AuctionClock.cs`; `api/TheYard.Api/VehicleWire.cs` (derived facts onto the wire);
  `src/lib/auction.ts` (all that remains client-side).
- **Sealed records everywhere data is data.** Every C# data shape (`Vehicle`,
  `VehicleFilter`, `BidState`, `SearchResult`) is a `sealed record`: records give
  value-based comparison, and sealing keeps that trustworthy, because record equality
  includes a hidden runtime-type check (`EqualityContract`) that inheritance would
  quietly poison. Sealing also states intent (a wire contract is not an extension point),
  lets the JIT devirtualize the generated `Equals` and `GetHashCode`, and is the
  low-regret default: unsealing later is non-breaking, sealing later is not. The payoff
  shows up in practice: determinism tests compare whole vehicle lists by value, and
  non-destructive `with` mutations power the bid overlay and the synthetic variants.
  *Where:* `api/TheYard.Data/Vehicle.cs`; `with` usage in
  `api/TheYard.Application/BidService.cs` and
  `api/TheYard.Infrastructure/SyntheticVehicleSource.cs`; value-equality assertions in
  `api/TheYard.Tests/SyntheticVehicleSourceTests.cs`.
- **Onion architecture that earns its layers.** Data (the pure records) has zero
  dependencies; Domain (the rules) depends only on Data; Application talks through ports
  (`IVehicleSource`, `IPhotoManifestSource`); Infrastructure adapts files; the host only
  binds and serializes. The proof it is not ceremony: the 100k scale-up is a decorator on
  a port (`SyntheticVehicleSource`) and nothing above it changed, and the test suite
  swaps in-memory fakes at the same seams.
  *Where:* `api/TheYard.Data/` to `api/TheYard.Domain/` to
  `api/TheYard.Application/` (`Ports.cs`, `InventoryService.cs`, `BidService.cs`) to
  `api/TheYard.Infrastructure/` to `api/TheYard.Api/Program.cs` (composition root);
  fakes in `api/TheYard.Tests/InventoryServiceTests.cs`. The whole picture is written
  down in `docs/ARCHITECTURE.md`, served as Architecture overview.
- **The documentation cannot drift from the code.** A record's samples are marked
  regions read out of the running container at request time, not pasted, and every
  record ends with a map of the files it decided. A test holds the document catalog to
  the sidebar's menu, another holds the changelog to the version being shipped.
  *Where:* `api/TheYard.Api/LiveSamples.cs`, `DocsCatalog.cs`,
  `api/TheYard.Tests/LiveSamplesTests.cs`, `DocsCatalogTests.cs`, `ChangelogTests.cs`.

## Notable Decisions

- **Domain rules live in pure functions**, fully separate from any framework: window
  derivation, increments, validation, and bid resolution in `api/TheYard.Domain`
  (unit-tested without hosting anything), reserve display and status recomputation in
  `src/lib/auction.ts` (unit-tested without rendering anything). Components stay thin.
- **The reserve amount is never rendered**, only its state (No reserve, Reserve met,
  Reserve not met), matching how real auction platforms guard seller data.
- **Price filtering and sorting use the competing price**, the high bid or the opening
  ask when there are no bids, so unbid vehicles do not sort as free.
- **Buy Now is a purchase, not a bid**: it does not inflate the bid count, and the
  vehicle presents as "Sold" with a purchase price everywhere.
- **One clock at the app root** (`useNow`) drives every countdown and status, so a card
  and its detail view can never disagree about liveness.
- **Query requests are debounced (500 ms) and cached (5 min, per query string,
  bounded)** in the data seam. Refresh paths (retry buttons, the periodic status-filter
  refresh) bypass the cache.
- **Nothing stale reaches a browser.** Vite names every bundle file by a hash of its
  contents, so `/assets/*` is cached for a year, and everything that can change under
  the same address says `no-cache`. Photos keep a one-day rule.
- **Photo mapping lives behind the API**: the server swaps the dataset's placeholder URLs
  for vendored stock photos, preferring same-make photos from the body-style pool.
  `data/vehicles.json` itself stays untouched, and the frontend renders whatever image
  URLs the API returns, as it would in production.
- **Every failure has one shape.** Rejected queries, rejected bids and unhandled
  exceptions all answer RFC 9457 ProblemDetails with the message in `detail` and a trace
  identifier; a React error boundary turns a render crash into a page with a way out and
  reports it to the Admin tab.

## Problems Hit and Solved

- **The dataset contradicted its own example.** The sample record shows
  `current_bid: 22800`, but 112 of the 200 real records have `current_bid: null`.
  Profiling the data before writing the types caught it; the fix rippled into the type
  (`number | null`), the minimum-bid rule (first bid meets the opening ask), and the
  "Starting bid" labels.
- **Cross-language rule drift bit twice.** With auction math mirrored in TypeScript and
  C#, the server and browser disagreed first across timezones, then on DST transition
  days. The durable fix was not a patch: the client now sends its literal local-midnight
  `anchor_ms`, and all derived facts moved server-side so the drift class cannot recur.
- **A passing test suite was proven blind by mutation.** Reordering the buy-now check
  ahead of bid validation left all tests green while breaking the rules, so the test that
  catches it now exists, along with a guard against `Infinity` instantly winning a
  buy-now (found by adversarial review).
- **The first end-to-end failure was the rules being smarter than the test.** Bidding the
  minimum on a vehicle whose `min_next_bid` crossed its `buy_now_price` triggered a
  legitimate instant win the test did not expect; the test now documents both outcomes as
  correct.
- **Vite's file watcher crashed on .NET build output.** Windows file locks in
  `api/**/obj` killed the dev server with `EBUSY`; fixed by excluding `api/**` from the
  watcher in `vite.config.ts`.
- **`npm start` raced its own browser tab.** Vite opens the browser in about 0.4 s while
  the API takes seconds to boot, so first paint could show a dead-API error. The initial
  load now retries quietly for up to 30 s, and the fix carries a regression test written
  from the actual bug report.
- **The deploy's first run failed on its own identity.** The federated credential subject
  GitHub presents is not the one the portal suggests; one `az` update fixed it, and the
  pipeline has rolled every version since with no human step. Recorded in ADR: The deploy
  pipeline.
- **A phone would not pick up a new stylesheet.** The old trick of appending a date to an
  import does not apply to a hashed bundle; the real answer was cache headers, measured
  before and after. Recorded in ADR: Cache headers.

## Testing

**API (236 xUnit tests, separate `TheYard.Tests` project):** one suite per onion layer.
Domain (photo gallery determinism and make preference, FNV-1a known vectors, auction
schedule bounds and boundaries, every filter rule, bid rules including increment tiers
and buy-now precedence), application (`InventoryService` and `BidService` with in-memory
fakes standing in for the file adapters), infrastructure (snake_case deserialization, the
synthetic 100k expansion's invariants, the real dataset and manifest), and integration
tests that boot the real host in memory (`WebApplicationFactory`) to verify endpoints,
filtering, sorting and paging parameters, the problem shape on every 400 and on a crash, the full bid
lifecycle, static image serving, cache headers, the document catalog, the live-sample
expander, the diagram pages, the changelog, and a persistence suite that places a bid,
disposes the application, starts a second one against the same database file and reads
the bid back, one test that points the connection string at a path which cannot be
opened to prove the site still serves its inventory when the store does not come up, and
two that hold the photo manifest and the image directory to the naming that responsive
images rely on, and an account suite that registers two people, has them outbid each
other, restarts the application and signs the first one back in to find their bid where
they left it, while checking that the token never appears in a response body and that a
wrong password says exactly what an unknown address says. Run with `npm run test:api`.

**Frontend (48 Vitest tests):** presentation logic only, since the API owns the rules.
Status recomputation from server windows, reserve states, formatting and countdowns, URL
and filter round-tripping, query-parameter mapping, the request cache (TTL, per key,
forced bypass, no caching of failures), the palette's contrast against WCAG AA,
including the two pairs a stylesheet composes that nobody had listed, and the account
seam, which translates the wire both ways, shows the server's own sentence when a
sign-in is refused, and holds no token anywhere. Run with `npm test`.

**End-to-end (43 Playwright tests):** the real stack. The landing page shows 100 of
100,000, filtering and tile navigation sync the URL both directions (including browser
Back and deep links), Load More appends a page, every sidebar section and document opens,
the diagrams open on their own pages, the Admin tab reports on the running system, a
browser error reaches it, a transient API failure recovers via the retry banner, the
phone drawer works at 375 pixels, the keyboard path walks from the skip link through
every view switch, a bid round-trips through the API, survives a reload, and resets,
the simulated room answers a bid so the high-bidder badge changes hands, the sign-in
form creates an account that survives a reload and a bid made under it appears in that
account's list, and axe holds eight views to WCAG 2.1 AA including both halves of the
account page. Run with `npm run test:e2e` (launches both servers itself, uses your
installed Chrome). All three suites run in CI on every push, and a green run on `main`
deploys.

ADR: The tests, explained walks all three suites for a developer new to the stack.

## What I'd Do With More Time

The four promises this section made when the build started have all shipped:

1. **Consistent coding and commenting styles, documented.** `docs/STYLE.md` (naming,
   layering, comments that explain why and how, never what) and `docs/ARCHITECTURE.md`
   (the onion, the wire contract, the derive-don't-store principle), both served under
   App Architecture, with an `.editorconfig` enforcing the mechanical half.
2. **Error handling.** RFC 9457 ProblemDetails on every failure with the message in
   `detail` and a trace identifier, one shape for queries and bids alike, structured
   JSON request logging, and a React error boundary that reports render crashes to the
   Admin tab. Recorded in ADR: Error handling.
3. **Code review.** A staff-level adversarial pass over the second day's work, every
   finding written down as kept, fixed or deferred, and the fixes shipped with tests.
   Recorded in ADR: The staff review.
4. **Hosting.** Live on Azure with HTTPS, a container built and rolled by GitHub Actions
   on every green push, and the production design (App Service behind Front Door) written
   in Bicep and deliberately undeployed, with the reason recorded.

Two more came off the list afterwards, on time that was no longer the deadline's:

5. **Application Insights.** Every request, dependency and exception the API handles is
   traced, browser errors included, and the Admin tab reads the last hour back with the
   container's own managed identity. The ingestion key is read from Azure at roll time
   and is nowhere in this repository. Recorded in ADR: Telemetry.
6. **Search indexing.** Each vehicle's searchable text is built once when the dataset
   loads and each query's tokens once when the filter compiles, rather than both being
   rebuilt for every one of the hundred thousand rows a scan touches. The scan halved,
   measured by a test that ships with it. Recorded in ADR: The search index.
7. **Keyboard access.** A skip link past the rail, focus that follows the view instead
   of falling to the document body, and a live region that names where you arrived.
   Recorded in ADR: Keyboard and screen reader.
8. **Simulated competing bidders.** A room that answers your bids through the same
   rules yours go through, with three limits that keep it a demo: it waits before
   answering, it stops at twice the opening ask, and it never buys a vehicle out from
   under you. The high-bidder badge can finally come off. Recorded in
   ADR: Competing bidders.

What is genuinely still open, in priority order:

- Real-time updates (Server-Sent Events) rather than the eight-second poll the
  competing bidders use now. The phase-one edge is a Netlify rewrite proxy, which
  buffers a streaming response, and the edge is not mine to change on a free tier;
  the reasoning is in ADR: Competing bidders
- Auth and per-user bid state; bids are persisted now, but they belong to one anonymous
  buyer, and the competing bidders are simulated rather than real people
- Durable storage across a container roll, which is done: the store is Azure SQL Database
  now rather than a file inside the container, so a bid outlives the deploy that was
  erasing it twice a day
- A virtualized grid once Load More accumulates thousands of rows
- An audit with a real screen reader, which is a person's job rather than a checklist's;
  the keyboard path is walkable and held by tests, and axe now holds every view to
  WCAG 2.1 AA on every run, which is the mechanical half of the same question
- A real image pipeline (srcset, blur-up placeholders) once photography replaces the
  representative stock photos

## Running with Docker

The quick version:

```
npm run docker        # build the image and serve everything at http://localhost:8080
npm run docker:stop   # stop it
```

That is all a visitor needs. The rest of this section explains what those two wrap.

The repo ships a multi-stage Dockerfile that builds the frontend, publishes the API,
and produces a single runtime image serving both on port 8080.

Build the image:

```
docker build -t theyard:local .
```

Run it:

```
docker run --rm -d -p 8080:8080 --name theyard theyard:local
```

Then open http://localhost:8080. The API serves the SPA with a fallback route, so deep
links to item URLs work. A container HEALTHCHECK probes `/healthz` every 30 seconds;
`docker ps` shows the container as healthy once the app is accepting traffic.

Stop it:

```
docker stop theyard
```

Notes:

- The final image runs as the base image's built-in non-root `app` user.
- Building needs no local Node or .NET; both toolchains live in intermediate stages.
- The final image carries the published API plus README.md, docs/, data/, and the source
  files the served records read their samples from, because the app documents itself at
  runtime.
- The image is built by the pipeline with `APP_VERSION` and `APP_COMMIT` baked in, which
  is what the footer and `/api/version` report.
