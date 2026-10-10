# Infrastructure overview

The Performance section opens with two overviews. This one is the machines a request crosses: what
each is and where it sits. For each it also gives the cost and what it adds to the clock. The
[Web overview](https://github.com/SteveStout/TheYard/blob/main/docs/performance/WEB-OVERVIEW.md) beside it is the page those machines serve and
the order it loads in. [Hosting](https://github.com/SteveStout/TheYard/blob/main/docs/hosting/HOSTING.md) holds the reasoning behind each
choice; this page is the performance reading of the same chain.

## In plain words

This page follows a web request through each machine it crosses, from the name lookup (DNS) to the databases, with the cost of each. Both sites and the rendering service that draws their pages share one small Azure machine (a Linux B2 App Service plan), and the whole bill at list price is $34.79 a month.

What that is worth: a developer can see that distance between regions is the largest delay, and the organization can read the whole running cost and where it goes on one page.

## The chain, hop by hop

| Hop | What it is | Where | Cost at list price | What it adds |
| --- | --- | --- | --- | --- |
| DNS | Two CNAME records at the registrar, both names pointing at one edge | Wix | $0.00 | one lookup on a cold visit |
| Edge | Netlify free plan: terminates HTTPS on one Let's Encrypt certificate, serves everything text-shaped as Brotli, keeps the hashed bundle files, forwards every API request unchanged, over HTTPS since 1.0.0.156, and since 1.0.3.104 sends each domain's page to the rendering service under that site's name | Netlify's network, HTTP/2 to the browser | $0.00 | the document's first byte came back in 272 to 302 ms on five of six cold visits on 19 September, measured on the container-group origin the day before the move to this plan; the edge holds a drawn page until it has all of it, so the stream the service sends arrives as one piece (ADR: A rendering service beside the API) |
| Page drawing | The rendering service, a third web app for containers on the same plan: a Node program that reads what an address needs from that site's API, draws the page with React's server renderer and sends it with the data in it, so the browser paints before any script runs; `/index.html` is still the API's own page, drawn in the browser, which is the way round while the service rolls | westus3, on the plan | $0.00 beyond the plan; 52 MiB idle and 136 MiB at its measured peak | the page's first byte through the domain 281 to 501 ms where a file read 196 to 241, and the inventory's speed index on a phone 3.5 s where it read 5.8 drawn in the browser, both on 8 October |
| Compute | One Linux B2 App Service plan carrying both sites as two web apps for containers, the same Docker image, and the rendering service as a third, 2 vCPU and 3.5 GB shared by the three, HTTPS from the edge | westus3, because westus2 refuses App Service on this subscription | $24.82 a month for all three ($0.034 an hour, 730 hours) | open a vehicle 0 ms, the filter values 0 ms, the listing page of 100 of 100,000 122 to 160 ms on the plan's shared core at B1, where a container group's own core read 51 |
| Registry | Azure Container Registry, Basic: one image tag per version, 8.7 GiB on 20 September of the 10 GiB included | westus2 | $5.07 a month ($0.1666 a day) | nothing on a request; an image pull on a roll, across one region |
| Relational store | Azure SQL Database, Basic, 5 DTU, 2 GB, Entra-only | westus3, the plan's own region since 20 September, and there because West US 2 refused to create a server | $4.90 a month ($0.161 a day) | 1 to 2 ms a round trip, where it was 39 from West US 2; a bid is two statements, 28 ms |
| Document store | Azure Cosmos DB, free tier, 1000 RU/s shared, no key exists | westus2, one region from the plan since 20 September | $0.00 | 38 to 40 ms a round trip, where it was 2 from West US 2; a bid is two operations, 89 ms |
| The catalogue | 100,000 vehicles, 82 MB against a 25 GB allowance | in the stores, loaded into each site's memory at start | $0.00 | reads that never leave the process |

**The whole bill at list price, with both sites and the rendering service running: $34.79 a month.** $24.82 of it is the plan;
the database is $4.90 and the registry $5.07, read off the Azure Retail Prices API on 20 September.
Until that day the compute was two container groups at $34.44 a month each and the same bill was
$78.85 a month; this page quoted $73.78, which left the registry out. The move took $56.47 a month off
it, to $22.38 on a B1 plan, and the plan moved to B2 on 8 October, $12.41 a month more, because its memory read 91
per cent of B1 at the median before the rendering service was added beside the two sites. What was priced and measured, with the B2 reading, is
[One plan, two sites](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-079-one-plan-two-sites.md). Crossing a region costs
$0.02 a gigabyte in either direction, and what crosses one is an image pull and one catalogue read
per roll, which is under a cent. The timings are the plan's own, from the proof run on both sites at
1.0.0.156 on 20 September, and the first byte is the Web overview's.

Both sites and the rendering service run on the one machine. The two container groups the sites ran on until 1.0.0.156 were stopped, which billed nothing, then kept as the way back until they were deleted on 7 October.

The plan's size is one parameter in the template, read here from this build; `AppServiceTemplateTests` holds every page that names the size to this line, so the next move of the plan cannot leave this one behind:

```live path=infra/appservice.bicep region=plan-size
```

## What the shape decides

- **Distance is the largest number on the page.** The two engines answer in the same time once the
  round trip is taken off each side; the 1 ms against 39 ms is a region boundary, it was 39 against 2
  the other way round until the compute changed regions, and it is paid once per statement
  ([The same performance, proven](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-067-same-performance-proven.md)).
- **The edge does the byte work, so the container does none of it.** Compression and the caching of
  hashed files were measured at the edge on 17 September, which is why server-side compression
  never made the list of changes on the
  [Performance overview](https://github.com/SteveStout/TheYard/blob/main/docs/performance/PERFORMANCE.md). What the edge costs and why it is
  free is [Edge economics](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-007-edge-economics.md).
- **Everything a first paint needs comes from one domain over one connection.** No third-party host
  appears on a cold visit, which is the Web overview's subject.
- **The template is what runs.** `infra/main.bicep` is the plan with its two sites and the rendering
  service's web app, deployed in incremental mode only, with Azure Front Door and the origin lock
  behind a parameter that stays off while the free trial refuses Front Door.

## Files

- [`infra/appservice.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/appservice.bicep): the plan and the two sites, 2 vCPU and 3.5 GB shared with the rendering service.
- [`infra/render.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/render.bicep): the rendering service's web app on the same plan.
- [`render/render.ts`](https://github.com/SteveStout/TheYard/blob/main/render/render.ts): what the service does with a page request.
- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard.yaml) and [`infra/aci-theyard-cosmos.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard-cosmos.yaml): the two container groups it replaced, deleted on 7 October and kept here as the record of what ran.
- [`netlify.toml`](https://github.com/SteveStout/TheYard/blob/main/netlify.toml) and [`edge/_redirects`](https://github.com/SteveStout/TheYard/blob/main/edge/_redirects): the edge.
- [`infra/main.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/main.bicep): what runs, with Front Door behind a parameter.
- [`docs/decisions/ADR-059-a-second-store-priced.md`](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-059-a-second-store-priced.md): what the second store costs and why it is free.
- [`docs/decisions/ADR-039-sql-server-backend.md`](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-039-sql-server-backend.md): the relational server, and the region it landed in.
