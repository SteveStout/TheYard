# ADR: Cache headers

Status: accepted, 2026-09-02, shipped as 1.0.0.20.

## In plain words

Browsers keep copies of files so pages load faster. This decides what they may keep: the code files, whose names change whenever their contents change, are kept for a year, and the page itself is checked with the server on every visit, so a new version reaches every visitor the next time they load the site.

What that is worth: users get fast pages and never a stale mix of old and new, and the organization ships a fix knowing every visitor sees it, with no support calls asking people to clear their cache.

## Context

Steve's phone kept showing the dark sidebar after the light one had
shipped. Measured on the live site before this change: the page itself,
`/`, was served with an ETag and a Last-Modified date and no Cache-Control
header at all. HTTP lets a browser reuse a response like that without
asking, for a stretch it estimates from the file's age (the heuristic
freshness rule in RFC 9111). A browser that held the old page could keep
using it, and the old page names the old bundle files, which it held under
the same rule. The API and the documents carried no cache headers either.

The old fix, from the days of hand-written script tags, was to append the
file's date to its address, `app.css?v=20260902`, so a changed file had a
changed address and no cache could confuse the two. A React app built with
Vite does the same thing without anyone typing a date: every bundle file
is named after a hash of its own contents, `assets/index-a1b2c3d4.css`,
and index.html is rewritten at build time to point at the new names. A
change in the CSS is a change in the file name, every build, with no hand
on it. What the hash cannot fix is index.html itself, which keeps its
address forever, and that is the file the phone was reusing.

## Decision

Three cache rules, set in one place by one middleware, chosen from the
shape of the address:

- **Hashed bundle files, `/assets/*`:** `public, max-age=31536000,
  immutable`, a year. Their names change when their contents change, so a
  stale copy is impossible, and a browser may keep them as long as it
  likes, which is what makes the second visit fast. Only a 200 gets this
  header; a missing file is never remembered for a year.
- **Everything that can change under the same address:** `no-cache`. The
  page, the API, the documents, the version. A browser may keep a copy but
  must ask before using it. The page answers those asks with 304 Not
  Modified from its ETag, which costs one round trip and no bytes.
- **The photo set, `/api/images/*`:** unchanged, one day, as it already
  was. The photos do not change between builds.

The date in the address is not needed and not used. The hash in the file
name is the same idea, done by the build, for every file, every time.

## In the code

The rules, read from this build
([`api/TheYard.Api/Composition/RequestPipeline.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/RequestPipeline.cs)):

```live path=api/TheYard.Api/Composition/RequestPipeline.cs region=cache-headers
```

The proof is
[`api/TheYard.Tests/CacheHeaderTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/CacheHeaderTests.cs):
the API, the documents and the version say no-cache; a missing bundle file
says no-cache rather than immutable; a photo keeps its day.

## Consequences

- A deploy is visible on the next load, on any device: the browser asks
  for the page, gets the new one, and the new one names new bundle files.
- A device that loaded the site before this shipped still holds the old
  page under the old rule until it reloads once; after that the rules
  above apply.
- The edge in front of the site was measured the same day as passing API
  responses through without caching them (Cache-Status fwd=miss), so the
  rules above reach the browser as written.
- The Admin tab, the version endpoint and the documents are fetched fresh
  every time. They are small, and being current is their whole job.

## Where it sits

The cache rules live in the host Api, in one inline middleware in Composition/RequestPipeline.cs (region cache-headers) that picks a rule from the shape of the address. The rule for every address is read in one place, with the photo set's one-day rule as the one exception. The cost is a round trip on every visit for the page and the API, answered with a 304 when nothing changed. A bundler that did not hash file names would force versioned addresses back into the page.

## Files

- [`api/TheYard.Api/Composition/RequestPipeline.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/RequestPipeline.cs): the middleware (region
  cache-headers above) and the photo set's own rule (region static-files).
- [`api/TheYard.Api/Composition/SpaRegistration.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/SpaRegistration.cs): the SPA fallback that answers only app routes.
- [`api/TheYard.Tests/CacheHeaderTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/CacheHeaderTests.cs): the proof, header by
  header.
- [`vite.config.ts`](https://github.com/SteveStout/TheYard/blob/main/vite.config.ts) and [`index.html`](https://github.com/SteveStout/TheYard/blob/main/index.html): the build that names
  every bundle file by its contents and rewrites the page to match.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): where the built bundle lands (`/app/wwwroot`).

## More of the code

The static file middleware, the photo set's own rule, and the fallback that
answers only app routes ([`api/TheYard.Api/Composition/RequestPipeline.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/RequestPipeline.cs)):

```live path=api/TheYard.Api/Composition/RequestPipeline.cs region=static-files
```

## Addendum, 2026-09-28 (1.0.3.35): the edge is warmed after every deploy

The year-long rule above is why a hashed file costs nothing the second time. The first time is the edge's miss: the edge asks the origin, and until it has the file it has nothing to serve. Measured on the Cosmos DB site from Missouri at 10:30 CDT, before this change: the activity card's four chunks took about 700 ms on a miss (about 630 ms of it waiting on the origin) and 33 ms once the edge held them. Because every deploy renames every hashed file, the first visitor after every deploy paid that miss on every chunk they opened.

- **Both deploys warm the edge after the site answers** (`scripts/warm-edge.mjs`, the step "Warm the edge" in `deploy.yml` and `deploy-cosmos.yml`). The script reads the build's file list off the site itself, through the public address: the page names its entry files and every script names the chunks it can load, so following those names reaches every file the build serves, with no manifest to publish. It asks for each file once, then for the Admin tab's public reads (`/api/admin/activity` for each window, `/api/health`, `/api/admin/machines`, `/api/admin/pages`), all with the deploy's own `TheYard-SelfRead/1 (deploy)` agent, so the Site activity card counts them as the site reading itself. A failure is a warning and never fails a deploy; `DeployWorkflowTests` holds the step to both workflows, after Verify.
- **What the warm-up can reach, and what it cannot.** The edge caches a proxied response at the node that served it; each node keeps its own. A GitHub runner reaches the node nearest the runner, which warms the files for visitors near that node and not necessarily for one in Missouri. Netlify's shared layer that would carry one warm read to every node, the `durable` cache directive, applies to responses from Netlify Functions only, not to proxied responses (Netlify's caching overview, read 28 September), so it does not fit an edge that proxies to Azure. What does reach a given place is a read from that place, so the ship that pushes from Steve's machine in Missouri runs the same script against both sites once the roll answers.

## Addendum, 2026-10-05 (1.0.3.76): measured again, and why the script still misses

Read from Missouri on 1.0.3.74, ten requests in a row for the hashed script on each site: six of twenty were hits and fourteen were misses, every miss marked `stored`. The headers this record sets are doing their job; the misses come from the edge keeping a separate cache per node, as the addendum of 28 September found, and from a site this quiet rarely landing on a warm one. Nothing in this repository can change that on the free plan. Netlify's shared `durable` layer still applies to its own Functions only. Serving the built files from the edge itself would end the misses, and it is priced in Edge deploy economics (ADR: Edge deploy economics, the addendum of 5 October). The numbers are on the Performance page.

## Addendum, 2026-10-05 (1.0.3.77): the catalogue's reads leave compressed

The Performance page said on 17 September that server-side compression was not needed, because the edge serves everything text-shaped as Brotli. That holds for the browser's side of the edge. It does not hold for the hop behind it: the edge forwards every API read to Azure, and the listing came back to it as about 106 KB of plain JSON, which the edge then compressed to about 16 KB. Measured from Missouri on 1.0.3.74, the whole of that answer took 0.54 to 0.80 s straight from Azure, and the listing through the edge took 0.34 to 2.43 s to its first byte.

So the container now compresses the catalogue's two reads, the listing and the filter values (`CatalogueReads`), with Brotli or gzip, for any caller that asks. The level is the optimal one, which is Brotli's quality 4. The fastest level was tried first and sent 44 KB of the 106, because the serializer writes the answer in pieces and each piece is flushed through the compressor; at quality 4 the flushes cost little, and a hundred vehicles take about a millisecond. Nothing else is compressed. Those two bodies carry no secret, which is what keeps them safe from the BREACH attack: it needs a secret and text an attacker chooses in the same compressed body, and a session here travels in a cookie header, which response compression does not touch. Every other address leaves as it did. `CompressionTests` holds both halves, and the numbers after the roll are on the Performance page.

## Addendum, 2026-10-05 (1.0.3.79): the edge's copy of the catalogue

Every API read said `no-cache`, so the edge forwarded each one to Azure and a visitor clicking through the listing waited on that hop every time. The listing and the filter values are now an exception for the edge alone. A 200 to either one carries, when the caller sent no cookie and no Authorization header and the answer sets no cookie, `Netlify-CDN-Cache-Control: public, max-age=15, stale-while-revalidate=15`: the edge may answer from its own copy for fifteen seconds and for fifteen more while it fetches a fresh one. The copy is kept per query string (`Netlify-Vary: query`) and per store (`Vary: X-Yard-Store`). The browser's rule does not change: `Cache-Control` still says `no-cache`, so a browser still asks every time.

What it costs, said plainly: a listing price or a sold flag can be up to thirty seconds behind on the listing grid. A vehicle's own page and its bids are not covered and are always read fresh, so the figure a buyer bids against is the live one. A signed-in request is answered fresh and never stored, so no shared copy holds anything a session produced. `CacheHeaderTests` holds the rule and the browser's unchanged `no-cache`; the numbers after the roll are on the Performance page.

## Addendum, 2026-10-09 (1.0.3.110): the edge's copy of a page

The same rule, on the pages the rendering service draws. A page request that carries no cookie is answered with `Netlify-CDN-Cache-Control: public, max-age=10, stale-while-revalidate=20` and `Netlify-Vary: query,cookie=theyard_session`, so the edge keeps a page drawn for nobody for ten seconds, per view, and a signed-in visitor is looked up under a key of their own. A request with any cookie is never kept. The browser still reads `no-cache` and asks every time. The decision and its cost are in ADR: A rendering service beside the API, the addendum on the edge's copy of a page; `render/render.ts` carries the rule and `CacheHeaderTests` holds the cookie's name to the API's.
