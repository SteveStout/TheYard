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
| Edge | Netlify free plan: terminates HTTPS on one Let's Encrypt certificate, serves everything text-shaped as Brotli, keeps the hashed bundle files, forwards every API request unchanged, over HTTPS since 1.0.0.156 | Netlify's network, HTTP/2 to the browser | $0.00 | the document's first byte came back in 272 to 302 ms on five of six cold visits on 19 September |
| Compute | One Linux B1 App Service plan carrying both sites as two web apps for containers, 1 vCPU and 1.75 GB shared, the same Docker image, HTTPS from the edge | westus3, because westus2 refuses App Service on this subscription | $12.41 a month for both ($0.017 an hour, 730 hours) | open a vehicle 0 ms, the filter values 0 ms, the listing page of 100 of 100,000 122 to 160 ms on the plan's shared core, where a container group's own core read 51 |
| Registry | Azure Container Registry, Basic: one image tag per version, 8.7 GiB of the 10 GiB included | westus2 | $5.07 a month ($0.1666 a day) | nothing on a request; an image pull on a roll, across one region |
| Relational store | Azure SQL Database, Basic, 5 DTU, 2 GB, Entra-only | westus3, the plan's own region since 20 September, and there because West US 2 refused to create a server | $4.90 a month ($0.161 a day) | 1 to 2 ms a round trip, where it was 39 from West US 2; a bid is two statements, 28 ms |
| Document store | Azure Cosmos DB, free tier, 1000 RU/s shared, no key exists | westus2, one region from the plan since 20 September | $0.00 | 38 to 40 ms a round trip, where it was 2 from West US 2; a bid is two operations, 89 ms |
| The catalogue | 100,000 vehicles, 82 MB against a 25 GB allowance | in the stores, loaded into each site's memory at start | $0.00 | reads that never leave the process |

**The whole bill at list price, with both sites running: $22.38 a month.** $12.41 of it is the plan,
$4.90 the database and $5.07 the registry, read off the Azure Retail Prices API on 20 September. Until
that day the compute was two container groups at $34.44 each and the same bill was $78.85; this page
quoted $73.78, which left the registry out. The move took $56.47 a month off it, and what was priced,
what was measured and why it is B1 are
[One plan, two sites](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-079-one-plan-two-sites.md). Crossing a region costs
$0.02 a gigabyte in either direction, and what crosses one is an image pull and one catalogue read
per roll, which is under a cent. The timings are the plan's own, from the proof run on both sites at
1.0.0.156 on 20 September, and the first byte is the Web overview's.

Both sites run on the one machine. The two container groups they ran on until 1.0.0.156 are stopped,
which bills nothing, and kept for a week as the way back.

## What the shape decides

- **Distance is the largest number on the page.** The two engines answer in the same time once the
  round trip is taken off each side; the 1 ms against 39 ms is a region boundary, it was 39 against 2
  the other way round until the compute changed regions, and it is paid once per statement
  ([The same performance, proven](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-067-same-performance-proven.md)).
- **The edge does the byte work, so the container does none of it.** Compression and the caching of
  hashed files were measured at the edge on 17 September, which is why server-side compression
  never made the list of changes on the
  [Performance overview](https://github.com/SteveStout/TheYard/blob/main/docs/PERFORMANCE.md). What the edge costs and why it is
  free is [Edge economics](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-007-edge-economics.md).
- **Everything a first paint needs comes from one domain over one connection.** No third-party host
  appears on a cold visit, which is the Web overview's subject.
- **The template is what runs.** `infra/main.bicep` is the plan and the two sites, deployed in
  incremental mode only, with Azure Front Door and the origin lock behind a parameter that stays
  off while the free trial refuses Front Door.

## Files

- [`infra/appservice.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/appservice.bicep): the plan and the two sites, 1 vCPU and 1.75 GB between them.
- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard.yaml) and [`infra/aci-theyard-cosmos.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard-cosmos.yaml): the two container groups it replaced, stopped and kept.
- [`netlify.toml`](https://github.com/SteveStout/TheYard/blob/main/netlify.toml) and [`edge/_redirects`](https://github.com/SteveStout/TheYard/blob/main/edge/_redirects): the edge.
- [`infra/main.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/main.bicep): what runs, with Front Door behind a parameter.
- [`docs/ADR-059-a-second-store-priced.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-059-a-second-store-priced.md): what the second store costs and why it is free.
- [`docs/ADR-039-sql-server-backend.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-039-sql-server-backend.md): the relational server, and the region it landed in.
