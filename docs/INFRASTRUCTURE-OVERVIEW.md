# Infrastructure overview

The Performance section opens with two overviews. This one is the machines a request crosses: what
each is, where it sits, what it costs and what it adds to the clock. The
[Web overview](https://github.com/SteveStout/TheYard/blob/main/docs/WEB-OVERVIEW.md) beside it is the page those machines serve and
the order it loads in. [Hosting](https://github.com/SteveStout/TheYard/blob/main/docs/HOSTING.md) holds the reasoning behind each
choice; this page is the performance reading of the same chain.

## The chain, hop by hop

| Hop | What it is | Where | Cost at list price | What it adds |
| --- | --- | --- | --- | --- |
| DNS | Two CNAME records at the registrar, both names pointing at one edge | Wix | $0.00 | one lookup on a cold visit |
| Edge | Netlify free plan: terminates HTTPS on one Let's Encrypt certificate, serves everything text-shaped as Brotli, keeps the hashed bundle files, forwards every API request unchanged | Netlify's network, HTTP/2 to the browser | $0.00 | the document's first byte came back in 272 to 302 ms on five of six cold visits on 19 September |
| Compute | One Azure Container Instance per site, 1 vCPU and 1.5 GB, the same Docker image, plain HTTP on 8080 behind the edge | westus2 | $34.44 a month each ($0.0405 a vCPU-hour, $0.00445 a GB-hour, 730 hours) | open a vehicle 1 ms, the filter values 0 ms since 1.0.0.142, the listing page of 100 of 100,000 51 ms |
| Relational store | Azure SQL Database, Basic, 5 DTU, 2 GB, Entra-only | westus3, one region from the container, because West US 2 refused to create a server | $4.90 a month ($0.161 a day) | 39 ms a round trip; a bid is two statements, 84 ms |
| Document store | Azure Cosmos DB, free tier, 1000 RU/s shared, no key exists | westus2, the container's own region | $0.00 | 2 ms a round trip; a bid is two operations, 10 ms |
| The catalogue | 100,000 vehicles, 82 MB against a 25 GB allowance | in the stores, loaded into the container's memory at start | $0.00 | reads that never leave the process |

**The whole bill at list price, with both sites running: $73.78 a month.** Two containers are $68.88
of it; with one container the bill is $39.34. The prices were read off the Azure Retail Prices API
on 19 September. The timings are the Performance overview's own, from the paired rounds at 1.0.0.94
and the wire measurements of 17 September, and the first byte is the Web overview's.

Both containers run. At 10:59 CDT on 19 September their health pages read 147,600 and 147,329
seconds of uptime, about 41 hours each, on 1.0.0.144.

## What the shape decides

- **Distance is the largest number on the page.** The two engines answer in the same time once the
  round trip is taken off each side; the 39 ms against 2 ms is a region boundary, and it is paid
  once per statement
  ([The same performance, proven](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-067-same-performance-proven.md)).
- **The edge does the byte work, so the container does none of it.** Compression and the caching of
  hashed files were measured at the edge on 17 September, which is why server-side compression
  never made the list of changes on the
  [Performance overview](https://github.com/SteveStout/TheYard/blob/main/docs/PERFORMANCE.md). What the edge costs and why it is
  free is [Edge economics](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-007-edge-economics.md).
- **Everything a first paint needs comes from one domain over one connection.** No third-party host
  appears on a cold visit, which is the Web overview's subject.
- **The production target is written and undeployed.** `infra/main.bicep` stands up App Service
  behind Azure Front Door with the origin locked; the free trial refuses Front Door, and the design
  costs nothing to keep.

## Files

- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard.yaml): the container group behind the Azure SQL site, 1 vCPU and 1.5 GB.
- [`infra/aci-theyard-cosmos.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard-cosmos.yaml): the second group, same image, other store.
- [`netlify.toml`](https://github.com/SteveStout/TheYard/blob/main/netlify.toml) and [`edge/_redirects`](https://github.com/SteveStout/TheYard/blob/main/edge/_redirects): the edge.
- [`infra/main.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/main.bicep): the production design, deliberately undeployed.
- [`docs/ADR-059-a-second-store-priced.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-059-a-second-store-priced.md): what the second store costs and why it is free.
- [`docs/ADR-039-sql-server-backend.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-039-sql-server-backend.md): the relational server, and the region it landed in.
