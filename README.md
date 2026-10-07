# TheYard

A used-vehicle auction platform I built and run on Azure: browse 100,000 vehicles, open one, and bid against a simulated room of other bidders.

**Live:** [theyard.stevenstout.biz](https://theyard.stevenstout.biz), and the same build on Cosmos DB at [theyard-cosmos.stevenstout.biz](https://theyard-cosmos.stevenstout.biz). The running version and commit: [/api/version](https://theyard.stevenstout.biz/api/version).

[![Deploy](https://github.com/SteveStout/TheYard/actions/workflows/deploy.yml/badge.svg)](https://github.com/SteveStout/TheYard/actions/workflows/deploy.yml)

## In plain words

TheYard is a used-vehicle auction site on Azure where you can browse 100,000 vehicles and bid against a simulated room of other bidders. The same code runs on two kinds of database at once (Azure SQL Database and Azure Cosmos DB). Every version must pass one set of tests (the gate) before it ships.

What that is worth: a developer can read each choice beside the code and the test that hold it, and the organization gets a live system where no version reaches users without passing the same checks.

## Tests, and the gate every version passes

The suites hold 826 xUnit tests, 393 Vitest tests at 1.0.3.92 and 149 Playwright tests. Every version reaches `main` through one gate, and the gate's results for 1.0.3.92 hold 2,383 test runs, with the xUnit suite booted on each store and the store-dependent browser specs run on Cosmos DB as well. I specify every test before the AI drafts the code against it.

| Suite | Framework | Count | What it covers |
| --- | --- | ---: | --- |
| API | xUnit | 826 | The bid rules, the auction schedule and every filter in Domain; the use cases in Application over hand-written fakes; the SQL and Cosmos DB adapters; and the real host booted in memory for every endpoint, the problem shape, accounts, persistence across a restart, the OpenAPI document and the served documents. |
| Frontend | Vitest | 393 at 1.0.3.92 | Presentation logic only, because the API owns the rules: status from server windows, formatting, the address bar round trip, the request cache, the account seam and the palette's contrast. |
| End to end | Playwright | 149 declared | The real stack in Chrome: the landing page, filters and Back, the sidebar and every document, the Admin tab, bids and the simulated room, accounts, the phone drawer, the keyboard path, and axe's eleven scans across nine views at WCAG 2.1 AA. |

**How 2,383 is counted**, from the gate's own results file for 1.0.3.92 ([`data/test-results.json`](https://github.com/SteveStout/TheYard/blob/main/data/test-results.json)): 393 Vitest tests, 891 xUnit tests on SQLite and the same 891 booted on Cosmos DB, the 7 that need the live Cosmos DB account, 153 browser runs on SQLite from the 149 declared tests (a loop runs one of them more than once) and 48 of those again on Cosmos DB, which is 2,383. The xUnit and browser counts above are read from the source; a test written after 1.0.3.92 first runs in the gate of the version that ships it.

**The rule.** Nothing reaches `main` without a green gate, and a red test stops the push. The gate runs on the machine that ships: format, lint and type checks, the SQL project, xUnit on SQLite, the seven live Cosmos DB tests, and then Vitest and the browser suite, one side after the other. Two passes run only when something they read changed: xUnit booted on Cosmos DB and the three store-dependent browser specs run when a change touches `api/`, `infra/cosmos/` or one of those specs, and otherwise the results file carries them forward from the version whose gate ran them, marked with that version ([ADR-068](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-068-the-five-minute-gate.md), the addendum of 22 September). The push is the deploy: [`deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml) and [`deploy-cosmos.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy-cosmos.yml) build the image and roll both sites, and each checks the version, `/readyz` and the store before it finishes. [`ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml) runs the same suites again on GitHub's runners for every push to main, beside the deploy and without holding it up, and for a pull request, which has had no gate.

**The time.** The target is five minutes (ADR-068), and every gate quoted here is over it: 831 seconds on 1.0.3.36 with every pass run, 657 seconds on 1.0.3.38 with the two store passes carried forward, 973 seconds on 1.0.3.39 with every pass run and 641 seconds on 1.0.3.59 with the two store passes carried forward, on a four-core laptop shared with the browser I work in. Every result ships with the version, test by test with its milliseconds; the Admin tab shows it. [ADR-021](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-021-tests-explained.md) walks the three suites for a developer new to the stack.

## Architecture and decisions

Ninety-three decision records carry the trade-off and the number behind each choice. A decision record (ADR) is one short document per decision: the context, what was decided, what it cost, and an addendum when it stopped being true. Six to read first:

- [ADR-088, onion and SOLID, how this codebase holds them](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-088-onion-and-solid.md): the rings, the ten tests that fail the gate when a dependency points outward, and what is kept simple on purpose.
- [ADR-075, the rules a change has to pass](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-075-the-rules-a-change-has-to-pass.md): every standing rule beside the test that holds it.
- [ADR-068, the five-minute gate](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-068-the-five-minute-gate.md): what runs before anything rolls, and what it costs.
- [ADR-066, one container, both stores](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-066-one-container-both-stores.md): the store toggle, Azure SQL and Cosmos DB behind one set of ports.
- [ADR-072, the code is public and the secrets are not](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-072-the-code-is-public-the-secrets-are-not.md): managed identity, and no key in the repository.
- [ADR-076, the API describes itself](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-076-the-api-describes-itself.md): the OpenAPI document and the [reference](https://theyard.stevenstout.biz/api/reference) built from the endpoints as mapped.

## Stack

- **API:** .NET 10 and C#, minimal APIs in an onion (Data, Domain, Application, Infrastructure, Api), its rings held by tests the build runs ([ADR-088, Onion and SOLID](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid)).
- **Front end:** React and TypeScript on Vite.
- **Stores:** Azure SQL Database and Azure Cosmos DB, chosen per request; SQLite locally.
- **Hosting:** Azure, described in Bicep.
- **Delivery:** GitHub Actions.

## Run it locally

Requires [Node 24](https://nodejs.org) and the [.NET 10 SDK](https://dotnet.microsoft.com/download), and Chrome for the browser suite. From a clean clone:

```
npm ci
npm start
dotnet test api/TheYard.slnx --filter "Store!=cosmos"
npm test
npm run test:e2e
```

`npm start` runs the API and the front end together and opens the browser. The xUnit filter leaves out the seven tests that need the live Cosmos DB account, the way CI does. The browser suite starts both servers itself.

## About this project

Built by one engineer, with AI drafting against my tests and records. I specify every test before the AI writes
the first draft of the code against it, three suites of tests run once per version inside the one gate before anything rolls (the counts are at the top of this page, held to the suites
by a test), and ninety-three decision records carry the trade-off and the number behind each choice. What went
wrong is recorded too. Read how it was governed in
[Built with AI](https://theyard.stevenstout.biz/?doc=built-with-ai), and what it all runs on, at
millisecond speeds on free-tier stores and one small container, in
[Performance](https://theyard.stevenstout.biz/?doc=performance).

TheYard is my portfolio implementation of a used-vehicle auction platform: browse a large
inventory, inspect a vehicle in detail, and place bids against a simulated room of other
bidders. It began as my submission to a company's take-home hiring exercise, in a fork
of their starter repository, and everything described below was built on that start (ADR:
The name, and how a rename was done without losing the history, says which and when). The frontend is a React app backed by a .NET 10 REST API that owns the data, the
search, and the auction rules, storing accounts and bids in Azure SQL Database or Azure
Cosmos DB, both reached with the container's managed identity, so what the container holds
for either store is an address and an authentication mode and nothing worth stealing.

The stores run on their free offers and the containers on trial credit, and every piece
is priced in its record rather than called free; it keeps serving when a database does
not: the catalogue falls back to files and the health endpoint says which store answered.
The Admin tab shows the running system reporting on itself, including every SQL statement
it has sent and how long the database took.

![The Yard inventory on a laptop: the docked sidebar of documents and decision records beside the vehicle grid](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-home.jpg)

Everything about how it is built and hosted is served from inside the running app, under
App Architecture, API Reference, SQL vs Cosmos DB, Performance, Diagrams, Style, Hosting, Built with AI, CI/CD
and Best Practices in the sidebar. Ninety-three decision records explain each choice, and the code samples in them are read from the running build
rather than pasted, so a record cannot drift from the code it describes. The shape of it:

[![TheYard infrastructure: the request path, the deploy path, and the designed production target](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/infrastructure.png)](https://theyard.stevenstout.biz/api/docs/diagrams/infrastructure)

*A preview. [Open the infrastructure diagram in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/infrastructure) to zoom in and follow it. The data flow has [its own drawing](https://theyard.stevenstout.biz/api/docs/diagrams/dataflow) too.*

## Using the API locally

On Windows the two tools install with
`winget install OpenJS.NodeJS.LTS Microsoft.DotNet.SDK.10`, and `npm run api` and `npm run dev` run the
two halves in separate terminals. With `npm start` running, open `http://localhost:5173`. The dev server proxies `/api` to the .NET API that serves
the inventory and the vehicle photos (`/api/images/...`). The inventory is **100,000
records**, deterministically synthesized at startup from the 200-record seed dataset
(`Inventory:TargetCount` in `api/TheYard.Api/appsettings.json`), so there is no giant
file in the repo. All filtering, sorting, and paging are server-side via LINQ over GET
parameters; the inventory view opens on the top 100 by auction time (live, ending soonest first):

```
GET /api/vehicles?make=Ford&status=live&sort=price-asc&limit=100
```

Parameters: `q` (matches every filterable field, including derived auction status),
`make`, `body_style`, `title_status`, `province`, `status`,
`min_condition`, `price_min`, `price_max`, `sort` (ending-soonest, price-asc,
price-desc, condition, most-bids), `limit` (default 100, max 500), `offset`. Responses
are an envelope `{ total, vehicles }`, each vehicle carrying server-derived auction
facts (`auction_starts_at`, `auction_ends_at`, `auction_status`, `min_next_bid`, and
`sold`, true once anybody has bought it).
Invalid `status` or `sort` values return 400 as RFC 9457 ProblemDetails
with the message in `detail`. `GET /api/vehicles/{id}` fetches one vehicle;
`GET /api/facets` feeds the filter dropdowns from the full dataset.

Bidding is server-side, validated by the domain rules, and belongs to a signed-in
account (`POST /api/auth/register`, `/login`, `/logout`; `GET /api/auth/me`):
`POST /api/vehicles/{id}/bids` `{ amount }` answers accepted or won, or 400
in the same problem shape; `POST /api/vehicles/{id}/buy-now`; `GET /api/bids` (the
signed-in account's standing on every vehicle it has bid on, an empty map signed out);
`DELETE /api/bids` (that account's start-over, nobody else's). Bids live in the
request's store, Azure SQL Database or Azure Cosmos DB on the live sites and SQLite
locally, are read into memory once at startup, and are overlaid on vehicles before
filtering, so price filters see what the UI shows. If the API is not running, the app
shows a clear error state with a retry.

The app also serves its own documentation and health:
`GET /api/docs/{slug}` (every document in the sidebar, live code samples expanded at
request time), `GET /api/docs/diagrams/{name}` (a diagram on its own zoomable page),
`GET /api/version` (the build and commit the footer shows), `GET /healthz` and
`GET /readyz` (liveness and readiness), `GET /api/health`, `GET /api/errors` and
`GET /api/admin/azure` (the Admin tab), and `GET /api/admin/activity` (visitor-days per day by
kind, people, scanners and the site's own reads, with the path to the resume and the referring hosts,
naming nobody; the per-visitor rows are off).

The API describes itself. `GET /api/openapi/v1.json` is an OpenAPI document built from the endpoints
as they are mapped, and [`/api/reference`](https://theyard.stevenstout.biz/api/reference) renders
it as a browsable reference with a request builder. Every public operation carries an operation id,
a summary and its responses, failures included; the bearer and cookie schemes are declared and the operations
that need a session are marked; the operator endpoints under `/api/admin/` are kept out of it. All
four rules are held by a test rather than by care, which is the point of ADR: The API describes
itself.

The .NET suite is measured as well as run, and published as an annotation on every CI run so
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
because two of its classes talk to Azure with a managed identity. CI has no Azure
credential and is never getting one; what is worth asserting about those two is that they
degrade rather than throw when the identity endpoint is not there, and that is tested
(ADR: Counting what the tests cover).

To refresh the photo set from Wikimedia Commons, run `node scripts/fetch_photos.mjs`.

## How It Was Built

Built test driven and domain-first: every test specified before the code it holds was written, and
the rules and their tests before any UI. It then grew in deliberate passes into a
demonstration of how I build production systems: the .NET API in onion architecture,
server-side filtering, sorting and paging over a 100,000-record synthetic dataset,
server-owned bidding rules, three test suites, CI, a container, a live host, and a
written architecture the code is reviewed against.

The work was pair-built with Claude Code throughout. I directed the scope and
the architecture and every product decision, and I am happy to walk through the reasoning
behind any line of it.

## Workflow

AI-assisted, verification-driven. I directed scope and architecture and the product decisions;
Claude Code implemented against tests I specified first, and nothing merged on trust: every change
ran the typechecker and all three suites, UI work was verified against real screenshots
at desktop, tablet and mobile widths, and features were driven end to end in a headless
browser before being called done. The build went domain-first (rules and tests before any
UI), then grew in deliberate passes: frontend, API, scale, bidding, hosting, then the
documentation and observability passes. Two adversarial reviews ran mid-stream, one
by several readers at once and one as a staff engineer would read it, and their findings
were fixed, tested, and in one case turned into a regression test. The living documentation is served inside the app, so the
walkthrough can happen without leaving it.

## Assumptions and Scope

- **`current_bid` is null for 112 of 200 vehicles** (the ones with `bid_count: 0`). The
  sample record shows a number, but the data is authoritative: the type is
  `number | null`. Before any bids exist, the minimum acceptable bid is the opening ask
  (no increment), a reserve cannot be met, and the UI labels the price "Starting bid".
- **Auction windows are derived, not read.** `auction_start` is synthetic, so each
  vehicle's id hashes to an end time spread across two days before to five days after
  "now", anchored to the UTC midnight that began the day, with a two to four day
  duration. Windows are stable across reloads within a UTC day and re-seed at 00:00
  UTC, so the inventory always shows a live mix of ended, live, and upcoming auctions.
- **One clock for everybody.** The anchor is the server's, so every visitor is in the
  same auction whatever their zone, and no request can name a day; until 1.0.0.112 the
  page sent its own local midnight and two visitors in different zones were in different
  auctions (ADR: Three readers with no memory of the project, the addendum on the clock).
- **A bid at or above the Buy Now price wins immediately at the Buy Now price**, even if
  it would fail the minimum-increment check: the instant-win rule takes precedence.
- **A purchase ends the auction for everybody.** Once anybody has bought a vehicle it is
  sold: every listing says so, and every further bid and every second Buy Now is refused
  with "This vehicle has been sold.", whatever the clock says. Until 1.0.0.110 only the
  buyer saw the sale and a second account could buy the same vehicle again (ADR: Accounts
  and per-user bids, the addendum on the second buyer).
- **Bids belong to accounts and persist.** Register or sign in and the bid is yours: it
  survives reloads and restarts, a simulated room of other bidders advances prices while
  a tab is open (ADR: Competing bidders), and two visitors can outbid each other. "Reset
  bids" is one person's start-over and leaves everybody else's bids standing.
- **Currency is CAD** (`en-CA`) since every listing is Canadian; one constant in
  `src/lib/format.ts` switches it.
- **Photos are representative, not the actual lot.** 50 free-license photos (10 per body
  style, modern generations) are fetched from Wikimedia Commons and mapped
  deterministically per vehicle id, preferring photos of the vehicle's own make. Real
  listings would use real lot photography; credits in
  `api/TheYard.Api/wwwroot/images/CREDITS.md`.
- **The API owns everything**: data, filtering, sorting, paging, photo mapping, auction
  scheduling, and bid validation. The browser formats and counts down; it also relays actions.
- Out of scope by design: seller tooling, checkout, payments, and real-time push;
  accounts, a database and per-user bids arrived on 2026-09-03 and are described below.

## Stack, in detail

- **Frontend:** React 19 + TypeScript (strict) on Vite 8; plain CSS via CSS Modules over
  design tokens in four sheets named for what they control (`src/styles/colors.css`, `sizes.css`, `typography.css`, `effects.css`); Vitest for tests. No component,
  icon, state or CSS libraries, and no router: icons are small inline SVGs and the
  address bar is the application state. Four runtime dependencies: react, react-dom,
  marked for rendering the served documents, and highlight.js for their code samples. The grounds and the text are Figma's Urban slate, gray
  and brown, under an accent of teal, dark green and gold, on glass panels over one soft
  watermark, with every text and ground pair measured against WCAG AA by a unit
  test, the watermark at its worst included. How it looks, and the rules that keep it
  looking that way, are the Style section in the sidebar (`docs/style/COLOR-STYLE.md`), with
  swatches drawn from the token sheet and the rules held by `StyleRulesTests` in the gate, and IBM Plex Sans, the one face since 1.0.3.19, served from the site's own `/assets`, with a system fallback; no external asset.
- **Backend:** .NET 10 minimal API in onion architecture (`api/`): `TheYard.Data`
  (the pure data records, no dependencies), `TheYard.Domain` (photo selection, auction
  schedule, filter, bid and standing rules), `TheYard.Application` (`Auction`, the use
  cases the endpoints ask, composing `InventoryService`, `BidService` and `MarketService`
  behind the auction's ports, and the operator's ports beside them), `TheYard.Infrastructure` (the EF Core
  adapters over Azure SQL Database or SQLite, the JSON readers that seed them, the synthetic scale-up), `TheYard.Api` (host,
  handlers in `Endpoints/`, every registration in `Composition/`, static images, the
  served documents, observability). `OnionTests` holds the direction in the build. Filtering is LINQ over GET parameters, including
  auction status; all auction math lives in Domain and travels on the wire, so the
  browser only formats. `src/lib/data.ts` is the frontend's single data seam.
- **Hosting:** a hand-authored multi-stage Dockerfile, an image in Azure Container
  Registry, and two web apps for containers, one per site, sharing one Linux B1 App Service
  plan at $12.41 a month, with Netlify's free tier as the TLS edge in front of them
  (ADR: One plan, two sites). GitHub Actions builds the image and rolls both sites on every green push.
  `infra/main.bicep` is what runs: the plan and the two sites with every setting, with Azure
  Front Door and the origin lock behind a parameter that stays off while the subscription
  refuses Front Door; the Hosting page explains both.
- **Database:** Azure SQL Database through EF Core, behind the same ports the JSON
  readers used to answer, with SQLite for local development and CI because neither has
  an Azure credential and neither should need one. And, since 1.0.0.89, Azure Cosmos
  DB behind the same ports: first as a second container, and since 1.0.0.94 side by
  side with the relational store in the same container, each site one store's site,
  with a Store bar at the top of every page that links to the other site at the same
  page, the request charge beside every operation on the Admin tab, a proof card that
  runs the same requests against both stores in paired rounds that alternate which store
  goes first, and the numbers in the records. There is no password anywhere: the
  server was created Entra-only, so it has no SQL login to have one, and the container
  authenticates as the managed identity it already carried. The Cosmos DB account has local
  auth disabled, so no key exists either. An account there is one document plus one
  address document, so an email stays unique without a cross-partition unique index
  (ADR: Accounts on a document store). The schema is a SQL project
  of hand-written DDL that compiles to a DACPAC and is the authority; EF maps to it and a
  conformance test fails the build when the two disagree. The running application
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
  yours. The session is a signed JWT, checked by ASP.NET Core's JWT bearer authentication and
  carried in a cookie the page cannot read, the bids are
  keyed on the person as well as the vehicle, and the account view lists what you have
  bid on and whether you are still winning. Two visitors can now outbid each other and
  both be told the truth about it.
- **Navigation:** every view is a GET URL. Filters, sorts, the open vehicle and the Admin
  tab are all shareable, deep-linkable and browser-Back friendly, with no router.
- **A sidebar that documents the app from inside it:** App Architecture, API Reference, SQL vs
  Cosmos DB, Performance, Diagrams, Style, Hosting, Built with AI, CI/CD, Best Practices, Decision
  Records, Changelog, About and Author, holding the
  architecture and style pages, the two stores side by side, the data flow,
  infrastructure, entity relationship, two-sites, store comparison and rings diagrams on their
  own zoomable pages, ninety-three decision records in one numbered index, the Bicep
  infrastructure, my resume, and How this was built, which says plainly that an AI agent
  wrote most of this and points at the evidence for judging what that produced.
- **An Admin tab:** timed health checks, the recent-errors list (server and browser
  alike), the site's own state and the plan it shares read from Azure with a managed identity, the
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
  *Where:* `src/lib/inventory.ts` (URL and filter serialization), `src/app/hooks/useAddressBar.ts`
  (pushState and popstate), `api/TheYard.Api/VehicleQueryParams.cs` (binding),
  `api/TheYard.Domain/VehicleFilter.cs` (the LINQ predicate).
- **Debounced, cached requests.** Filter changes debounce 500 ms so typing does not
  hammer the API, and responses are cached per query string (5-minute TTL, bounded).
  Cache hits skip the debounce entirely: the delay only exists to protect the server,
  and a hit never touches it.
  *Where:* `src/lib/data.ts` (cache, `peekVehicles`), `src/app/hooks/useInventory.ts` (the debounced
  fetch effect), `api/TheYard.Api/Composition/RequestPipeline.cs` (cache headers).
- **Server-side pagination at scale.** 100,000 records, but the wire only ever carries a
  page: an envelope of `{ total, vehicles }` with `limit` and `offset`, a landing page of
  the top 100 by auction time, and Load More to walk deeper.
  *Where:* `api/TheYard.Application/InventoryService.cs` (`Search`),
  `api/TheYard.Infrastructure/SyntheticVehicleSource.cs` (the 100k expansion),
  `src/app/hooks/useInventory.ts` (`loadMore`).
- **A search that does its work once.** Both halves of a free-text comparison are
  precomputed: each vehicle's searchable text when the dataset loads, each query's
  tokens when the filter compiles. The version before this rebuilt both inside the
  loop, so one search allocated a lowercase copy of nine fields a hundred thousand
  times for a query typed once. The scan went from a 37 ms median to 17 ms across the
  full dataset, measured with a stopwatch over three runs and tabled in ADR: The search
  index; the suite holds the weaker, repeatable claim, that the indexed scan is never
  slower than the rebuilt one, because a benchmark that asserts a millisecond is a test
  that fails on a busy machine. The
  auction status stays out of the index on purpose because the clock decides it, and
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
  poison without a compile error. Sealing also states intent (a wire contract is not an extension point),
  lets the JIT devirtualize the generated `Equals` and `GetHashCode`, and is the
  low-regret default: unsealing later is non-breaking, sealing later is not. The payoff
  shows up in practice: determinism tests compare whole vehicle lists by value, and
  non-destructive `with` mutations power the bid overlay and the synthetic variants.
  *Where:* `api/TheYard.Data/Vehicle.cs`; `with` usage in
  `api/TheYard.Domain/StandingRules.cs` (the bid overlay), `api/TheYard.Application/BidService.cs` and
  `api/TheYard.Infrastructure/SyntheticVehicleSource.cs`; value-equality assertions in
  `api/TheYard.Tests/SyntheticVehicleSourceTests.cs`.
- **Onion architecture that earns its layers.** Data (the pure records) has zero
  dependencies; Domain (the rules) depends only on Data; Application talks through ports
  (`IVehicleSource`, `IPhotoManifestSource`, `IBidStore`); Infrastructure and its Cosmos DB twin
  adapt the stores and the files; the host only
  binds, asks `Auction` and serializes. The proof it is not ceremony: the 100k scale-up is a decorator on
  a port (`SyntheticVehicleSource`) and nothing above it changed, and the test suite
  swaps in-memory fakes at the same seams. The build holds the rings: `OnionTests` reads
  the compiled assemblies with NetArchTest and fails on any dependency that points outward
  or an endpoint that reaches a store, ten rules in all ([ADR-088, Onion and SOLID](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid)).
  *Where:* `api/TheYard.Data/` to `api/TheYard.Domain/` to
  `api/TheYard.Application/` (`Ports.cs`, `Auction.cs`, `InventoryService.cs`, `BidService.cs`) to
  `api/TheYard.Infrastructure/` to `api/TheYard.Api/Composition/` (composition root, with
  `Program.cs` its table of contents) and `api/TheYard.Api/Endpoints/` (the handlers);
  fakes in `api/TheYard.Tests/InventoryServiceTests.cs`; the rules in
  `api/TheYard.Tests/OnionTests.cs`. The whole picture is written
  down in `docs/app-architecture/ARCHITECTURE.md`, served as Architecture overview.
- **The documentation cannot drift from the code.** A record's samples are marked
  regions read out of the running container at request time, not pasted, and every
  record ends with a map of the files it decided. A test holds the document catalog to
  the sidebar's menu, another holds the changelog to the version being shipped.
  *Where:* `api/TheYard.Api/LiveSamples.cs`, `DocumentationCatalog.cs`,
  `api/TheYard.Tests/LiveSamplesTests.cs`, `DocumentationCatalogTests.cs`, `ChangelogTests.cs`.

## Notable Decisions

- **Domain rules live in pure functions**, fully separate from any framework: window
  derivation, increments, validation, and bid resolution in `api/TheYard.Domain`
  (unit-tested without hosting anything), status recomputation in
  `src/lib/auction.ts` (unit-tested without rendering anything). Components stay thin.
- **The reserve amount never leaves the server**, only its state (No reserve, Reserve met,
  Reserve not met). The server works the state out from the reserve and the current bid
  and sends `reserve_state` on every vehicle, so the seller's number is not on the wire
  and the panel shows what the server said.
- **Price filtering and sorting use the competing price**, the high bid or the opening
  ask when there are no bids, so unbid vehicles do not sort as free.
- **Buy Now is a purchase, not a bid**: it does not inflate the bid count, the vehicle
  presents as "Sold" with a purchase price everywhere and to everybody, and it takes no
  further bid from anyone.
- **Simultaneous bids are serialized.** Bidding is read, decide, write; each step being
  atomic does not make the sequence atomic: two bids on the same vehicle could both pass the
  rules and the lower one land second. One gate lets one bid through at a time, held across
  the store's answer (ADR: The ports learn to wait).
- **One clock at the app root** (`useNow`) drives every countdown and status so that a card
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
  days. The durable fix was not a patch: all derived facts moved server-side so the drift
  class cannot recur. For a while the client still sent its local midnight as the anchor
  the server derived from, which made the auction a function of who was looking; since
  1.0.0.112 the clock is the server's alone.
- **One vehicle had two buyers.** Buy Now recorded the sale on the buyer's own bid and
  nowhere else, so a second account saw a live auction, bid the Buy Now price and was
  told it had won the same vehicle; the room already knew better and refused to bid on
  sold vehicles, and nobody had asked the rules the same question. Found by a fresh-eyes
  review of the code with no memory of the project, held by tests at the rules, the
  service and the API, and recorded in ADR: Accounts and per-user bids.
- **A passing test suite was proven blind by mutation.** Reordering the buy-now check
  ahead of bid validation left all tests green while breaking the rules, so the test that
  catches it now exists; the companion worry from the same review, a non-numeric amount
  such as `Infinity` winning a buy-now, is refused by the model binder before the rules
  see it, and the rules take an integer.
- **The first end-to-end failure was the rules being smarter than the test.** Bidding the
  minimum on a vehicle whose `min_next_bid` crossed its `buy_now_price` triggered a
  legitimate instant win the test did not expect; the test now documents both outcomes as
  correct.
- **Vite's file watcher crashed on .NET build output.** Windows file locks in
  `api/**/obj` killed the dev server with `EBUSY`; fixed by excluding `api/**` from the
  watcher in `vite.config.ts`.
- **`npm start` raced its own browser tab.** Vite opens the browser in about 0.4 s while
  the API takes seconds to boot, so first paint could show a dead-API error. The initial
  load now retries without showing an error for up to 30 s, and the fix carries a regression test written
  from the actual bug report.
- **The deploy's first run failed on its own identity.** The federated credential subject
  GitHub presents is not the one the portal suggests; one `az` update fixed it, and the
  pipeline has rolled every version since with no human step. Recorded in ADR: The deploy
  pipeline.
- **A phone would not pick up a new stylesheet.** The old trick of appending a date to an
  import does not apply to a hashed bundle; the real answer was cache headers, measured
  on both sides of the change. Recorded in ADR: Cache headers.

## Testing

**API (826 xUnit tests, separate `TheYard.Tests` project):** one suite per onion layer.
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

**Frontend (393 Vitest tests at 1.0.3.92):** presentation logic only, since the API owns the rules.
Status recomputation from server windows, reserve states, formatting and countdowns, URL
and filter round-tripping, query-parameter mapping, the request cache (TTL, per key,
forced bypass, no caching of failures), the palette's contrast against WCAG AA,
including the two pairs a stylesheet composes that nobody had listed, and the account
seam, which translates the wire both ways, shows the server's own sentence when a
sign-in is refused, and holds no token anywhere. Run with `npm test`.

**End-to-end (149 Playwright tests):** the real stack. The inventory view shows 100 of
100,000, filtering and tile navigation sync the URL both directions (including browser
Back and deep links), Load More appends a page, every sidebar section and document opens,
the diagrams open on their own pages, the Admin tab reports on the running system, a
browser error reaches it, a transient API failure recovers via the retry banner, the
phone drawer works at 375 pixels, the keyboard path walks from the skip link through
every view switch, a bid round-trips through the API, survives a reload, and resets,
the simulated room answers a bid so the high-bidder badge changes hands, the sign-in
form creates an account that survives a reload and a bid made under it appears in that
account's list, and axe runs eleven scans across nine views at WCAG 2.1 AA, including both
halves of the account page. Run with `npm run test:e2e` (launches both servers itself, uses your
installed Chrome). All three suites run once per version in the ship gate, and only a green gate
pushes to `main`, which deploys. The seven tests that need the real Cosmos DB account run in the
same gate, beside the whole API suite booted a second time on Cosmos DB; CI, which runs the
suites on a pull request and has no Azure credential, filters those seven out.

ADR: The tests, explained walks all three suites for a developer new to the stack.

## What I'd Do With More Time

The four promises this section made when the build started have all shipped:

1. **Consistent coding and commenting styles, documented.** `docs/app-architecture/STYLE.md` (naming,
   layering, comments that explain why and how, never what) and `docs/app-architecture/ARCHITECTURE.md`
   (the onion, the wire contract, the derive-don't-store principle), both served under
   App Architecture, with an `.editorconfig` enforcing the mechanical half.
2. **Error handling.** RFC 9457 ProblemDetails on every failure with the message in
   `detail` and a trace identifier, one shape for queries and bids alike, structured
   JSON request logging, and a React error boundary that reports render crashes to the
   Admin tab. Recorded in ADR: Error handling.
3. **Code review.** An adversarial pass over the second day's work, read the way a staff
   engineer reads a pull request, every finding written down as kept, fixed or deferred,
   and the fixes shipped with tests.
   Recorded in ADR: The staff review.
4. **Hosting.** Live on Azure with HTTPS, a container built and rolled by GitHub Actions
   on every green push, and the App Service plan and both sites described in Bicep, with
   Azure Front Door behind a parameter that stays off while the subscription refuses it,
   the reason recorded.

Four more came off the list afterwards, on time that was no longer the deadline's:

5. **Application Insights.** Every request, dependency and exception the API handles is
   traced through Azure Monitor OpenTelemetry, browser errors included, and the Admin tab reads the last hour back with the
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
  competing bidders use now. The phase-one edge is a Netlify rewrite proxy that
  buffers a streaming response, and the edge is not mine to change on a free tier;
  the reasoning is in ADR: Competing bidders
- Real people at the other end of a bid: accounts and per-user bids exist, and the
  competing bidders are still simulated, one room per container
- One writer per store: both sites open both stores and each keeps its own
  standing in memory, so a bid placed through one is not seen by the other until it
  restarts, which ADR: One container, both stores calls out and nothing yet enforces
- A virtualized grid once Load More accumulates thousands of rows
- An audit with a real screen reader, which is a person's job rather than a checklist's;
  the keyboard path is walkable and held by tests, and axe now holds every view to
  WCAG 2.1 AA on every run, which is the mechanical half of the same question
- A real image pipeline (blur-up placeholders) once real photography replaces the
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

Then open `http://localhost:8080`. The API serves the SPA with a fallback route, so deep
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
