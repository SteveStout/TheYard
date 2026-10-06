# Performance on the smallest machine that will hold it

Every number on this page was measured by the running application against itself, on the machine that
serves this site, and every one of them links to the record that holds the method. Nothing here is a
benchmark run on a laptop and quoted afterwards.

The claim this page makes is narrow and checkable: **a hundred thousand vehicles, two different database
engines, and page work measured in milliseconds, on free-tier data stores and the smallest machine that
will hold it: since 20 September 2026, one Linux B1 App Service plan carrying both sites for $12.41 a month.**

## In plain words

This page measures how fast the site runs on the smallest Azure machine that will hold it, with a hundred thousand vehicles in two different databases (Azure SQL Database and Azure Cosmos DB). The two answer in the same time once the trip to each one is taken off, so any gap comes from where each one sits.

What that is worth: a developer gets methods and numbers they can rerun from the Admin tab, and the organization sees the whole bill, $22.38 a month at list price, next to the speed it buys.

## What it runs on

| Resource | What it is | What it costs |
| --- | --- | --- |
| Azure Cosmos DB | Free tier, 1000 RU/s shared, local auth disabled so no key exists | **$0.00 a month** |
| Azure SQL Database | Basic, 5 DTU, 2 GB, Entra-only (the serverless free database beside it paused on 14 September, below) | $4.90 a month |
| Compute | One Linux B1 App Service plan, 1 vCPU and 1.75 GB, shared by both sites as two web apps for containers | **$12.41 a month** for both at list price in westus3, $0.017 an hour over 730 hours |
| Registry | Azure Container Registry, Basic, one image tag per version, 8.7 GiB on 20 September of the 10 GiB the tier includes | $5.07 a month ($0.1666 a day) |
| Edge and TLS | Netlify free plan, 300 build credits a month | **$0.00 a month** |
| Storage, 100,000 vehicles | 82 MB against a 25 GB allowance | **$0.00** |

**The whole bill at list price is $22.38 a month: $12.41 of compute, $4.90 of database and $5.07 of
registry**, read off the Azure Retail Prices API on 20 September. Until that day the compute was two
Azure Container Instances, one per site at $34.44 a month each, and the same bill was $78.85 a month; the move took
$56.47 a month off it, 72 per cent, and the record prices every option that was on the table
([One plan, two sites](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-079-one-plan-two-sites.md)). This page quoted
$73.78 until then, which left the registry out; it has been $5.07 a month all along. What the
subscription is actually charged, day by day with Azure's forecast for the month, is on the Admin tab's
[cost card](https://theyard.stevenstout.biz/?view=admin&card=spend), read from Azure Cost Management once an
hour: $47.18 for September by the 30th, more than the list price because the two container groups ran until
the 20th ([What Azure charges](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-087-what-azure-charges.md)). The
[Infrastructure overview](https://github.com/SteveStout/TheYard/blob/main/docs/performance/INFRASTRUCTURE-OVERVIEW.md) prices every hop and says
what each adds to the clock, and the [Web overview](https://github.com/SteveStout/TheYard/blob/main/docs/performance/WEB-OVERVIEW.md) is the page
they serve and the order a first visit loads it in.

The plan and its two sites are the whole of the compute, and this is what differs between the two, read
from the running build. Everything else, every setting included, is one list in the same file, and the
two keys are filled from repository secrets at roll time
([The code is public and the secrets are not](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-072-the-code-is-public-the-secrets-are-not.md)).

```live path=infra/appservice.bicep region=two-sites
```

Sources: [A second store, priced](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-059-a-second-store-priced.md),
[Edge economics](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-007-edge-economics.md), [The partition key](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-058-the-partition-key.md).

## What a first visit costs, and what 1.0.3.0 took off it

Measured on both live sites on 2026-09-22 with a cold cache, a phone 375 pixels wide on a throttled connection and a desk 1280 pixels wide:

| View | Requests | Over the wire | Load event |
| --- | --- | --- | --- |
| Landing, phone | 17 | 185 KB | 1.0 s |
| Inventory, phone | 15 | 212 KB | 0.6 s |
| Author, phone | 33 | 794 KB | 0.5 s |
| Author, desk | 33 | 1,574 KB | 0.4 s |

Two things in that reading were work nobody asked for. The landing page's slowest request was `/api/vehicles`, 450 ms on the SQL site and 897 ms on the Cosmos DB one, for a page that shows no vehicle; the filter options were fetched beside it. And the Admin tab, about four thousand lines of cards and charts and the largest view in the app, was in the first bundle every visitor downloaded, whether or not they ever opened it.

So the catalogue and the filter options are asked for when a view that shows them opens, and the Admin tab is a chunk of its own, fetched on `?view=admin` and cached for a year like every other hashed file. `landing.spec.ts` holds the first of those: the landing page asks for neither, and opening the inventory asks for both.

**1.0.3.1, the type.** The four Poppins files are named in `src/styles/fonts.css`, so a cold browser only learns about them after it has fetched the stylesheet: measured on the live sites, they started about 150 ms behind the CSS and took 140 to 165 ms each. The build now writes a preload link for each into the head, named from its own hashed output, so they start with the stylesheet. All four paint on the first screen, body, medium, semibold and bold, so nothing there is speculative, and they are 32 KB in all.

The Author page's weight is photographs, and they are cut per width: a phone is handed the 480 cuts and never a file wider than 960. They load eagerly on purpose (ADR: The sidebar, the addendum on the author's section): lazy loading them made them flash on a phone while the dialog scrolled.

**1.0.3.3, the vehicle photographs.** The same change on the inventory's fifty pictures: the card's 480 set went from 1,281 KB to 711 and the 1280 set from 8,131 KB to 4,500, each AVIF encoded from the original rather than from a copy.

**1.0.3.2, the photographs.** They are cut to AVIF as well now and offered before the WebP (ADR: Responsive photos, the addendum on AVIF), which halves the set: 1,962 KB to 992 at 960 wide, 586 KB to 296 at 480. The originals were not touched, and a browser too old for AVIF still gets the WebP and then the JPEG.

## What that buys, measured

The application times itself. `POST /api/admin/proof` runs eight paired rounds of everything a visitor
does, alternating which store goes first so the ordering cannot flatter either one, and the Admin tab
shows the result. These are the medians from the live site's own container at 1.0.0.94, when each site
had a container group of its own in West US 2; the same run on the plan is further down, and what it did
to these columns is the best evidence this page has:

| What a visitor does | Azure SQL Database | Azure Cosmos DB |
| --- | --- | --- |
| Open a vehicle | **1 ms** | **1 ms** |
| Load the filter values | **12 ms** | 29 ms |
| The listing page, 100 of 100,000 | **51 ms** | 56 ms |
| Place a bid | 84 ms, 2 statements | **10 ms**, 2 operations, 6.52 RU |
| Raise a bid | 85 ms, 2 statements | **9 ms**, 2 operations, 11.29 RU |
| Sign in | 122 ms, 1 statement | **82 ms**, 2 operations, 2.00 RU |
| Register | 233 ms, 3 statements | **133 ms**, 4 operations, 13.04 RU |

The pages that never touch a store are the same on both, to the millisecond.

The rounds themselves, from the code that runs them: one account per store for the life of the process,
the same paths in the same order, and the store that goes first alternating every round.

```live path=api/TheYard.Api/ProofRunner.Rounds.cs region=rounds
```

Source: [The same performance, proven](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-067-same-performance-proven.md),
[Measuring both stores](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-064-measuring-both-stores.md).

## The honest part, and it is the interesting part

**The two engines are not different. The distance to them is.**

On 3 of the 8 paths the two stores answer in the same time. On the other 5 the whole difference is the
round trip to the store: **39 ms to Azure SQL Database and 2 ms to Azure Cosmos DB.** Take one round trip
per statement or per operation off each side and what is left is within a few milliseconds either way.

The relational server sits one region away from the container and every statement crosses that gap. The
document account is in the container's own region. A bid is two statements or two operations, so the
relational side pays the gap twice and the card shows exactly that.

**That is a hosting fact, not a database fact**, and it is worth saying plainly rather than quoting the
faster column and letting a reader assume the engine won. The same run was repeated on the second
container, whose default store is the document one, and it produced the same shape on every row.

### Then the host moved, and the columns swapped

On 20 September both sites moved to one App Service plan in West US 3, the region the relational server
is in, because West US 2 refuses App Service on this subscription
([One plan, two sites](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-079-one-plan-two-sites.md)). Nothing about either
engine changed and no line of data access changed. The same proof, run on both sites at 1.0.0.156 within
the hour, read **1 to 2 ms to Azure SQL Database and 38 to 40 ms to Azure Cosmos DB**: the two numbers
above, on the other sides.

| What a visitor does | Azure SQL Database | Azure Cosmos DB |
| --- | --- | --- |
| Open a vehicle | **0 ms** | **0 ms** |
| Load the filter values | **0 ms** | **0 ms** |
| Place a bid | **28 ms**, 2 statements | 89 ms, 2 operations, 6.52 RU |
| Raise a bid | **19 ms**, 2 statements | 83 ms, 2 operations, 11.29 RU |
| Start over | **9 ms**, 1 statement | 91 ms, 2 operations, 7.80 RU |

Every store-bound row flipped, by the round trip and by nothing else: the card's own verdict on those
three rows is that the whole difference is the round trip to the store. The request units did not move
at all, 6.52 and 11.29 on both hosts, because a request unit is what the engine charged and the engine
did not change. A page that had quoted the faster column as a fact about a database would have had to
be rewritten that day. This one only had to be added to.

What the smaller machine did cost is processor. The plan's one core is shared by both sites and
measures as a slower core than the one each container group had to itself, and the two paths that are
processor and not store show it: across two warm runs on each site the listing page of 100 of 100,000
read 122 to 160 ms where it had read 51, and a sign-in, which is a deliberately expensive password hash,
read 430 to 610 ms where it had read 82 to 122.
From a desk in Missouri through the edge the listing answered in a median of 418 ms on one site and
439 ms on the other over ten reads each, which is what a visitor waits. That is the trade, stated:
$56 a month against about a tenth of a second on the listing and four tenths on a sign-in.

The verdict is arithmetic, and here it is: the median of the paired differences, one round trip per
operation taken off each side, and the rule for how far apart two medians may sit and still be called
the same.

```live path=api/TheYard.Api/ProofResult.cs region=verdict
```

Read again on 13 September, off both containers' own cards from runs on 1.0.0.114 made within a quarter of
an hour of each other: 39 ms and 38 ms to Azure SQL Database, 2 ms to Azure Cosmos DB, 3 of 8 paths the
same, 4 more differing by exactly the round trip, and one row, Register, differing by more: 231 ms against
188 on one container and 453 against 103 on the other, on a single sample each. One sample at that size on
a serverless database that pauses when idle says nothing about a statement's cost either way, and the card
says "1 sample" beside it. Bid write read 84 ms against 13 and 84 against 11, bid raise 87 against 11 and 83
against 11, within a few milliseconds of the table above in both directions. The table keeps the 1.0.0.94
run because it is the one the record walks through row by row; the later cards are quoted so a reader can
see the shape hold three versions and twenty days later, with the site activity writer added in between
([Site activity, and the line an address does not cross](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-071-site-activity.md)).

## Where the milliseconds actually came from

Three decisions, each with its number.

**The index is paid for on every write and earns only on the queries that use it.** Leaving the document
store's indexing policy at its default charges **16.07 request units a document** on the bulk seed against
**8.84** with the policy trimmed: 21 minutes against 13, and 8,407 documents refused by throttling that the
tuned seed never saw. The same seven queries then cost within half a request unit of each other on both.
([Cosmos DB explained](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-065-cosmos-db-explained.md))

**A partition key is chosen by its worst case, not its average.** `/make` has fifteen values with the
largest at nine per cent, so the biggest logical partition at a hundred thousand documents is about
**7 MB against a 20 GB cap** and no partition is hot. `/body_style` would have put 53 per cent of every
read on one partition. ([The partition key](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-058-the-partition-key.md))

Both decisions are one file, the definition the container was created from, with the reasoning in it:

```live path=infra/cosmos/catalogue.json region=*
```

**Only the default store warms before serving.** Loading both catalogues at start-up doubled every test
application's memory and turned a two-minute suite into a thirty-minute crawl that looked like a hang.
([One container, both stores](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-066-one-container-both-stores.md))

The other store warms on the first request that names it, and no request thread ever waits on a load
already in progress:

```live path=api/TheYard.Api/Composition/RequestPipeline.cs region=warm-before-reading
```

**The search is an index, and the page's files are cached for a year.** A text scan over the hundred
thousand rows roughly halved through a prebuilt index, 36 to 45 ms down to 14 to 21 ms across three runs,
measured by a test that ships with it and honest about the rest: the whole request improved by about
the same twenty milliseconds, since ordering and serialising a page is most of what remains
([The search index](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-025-search-index.md)). And
every bundle file is named by a hash of its contents, so a browser keeps it for a year and a returning
visitor fetches the small HTML page and the data, never the bundle again ([Cache headers](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-015-cache-headers.md)):

```live path=api/TheYard.Api/Composition/RequestPipeline.cs region=cache-headers
```

## What the page costs on the wire, measured on 17 September

The table above is the server timing itself. This section is the other half, the page as a browser
receives it, measured against the live site on 1.0.0.139 before anything was changed, by the runner
on Steve's machine with the repository's own Playwright Chromium, a cold cache on every round, and
`Cache-Control: no-cache` on every direct read (the log is `lane0917-963-perflane-measure.log`).
Every change below shipped as a version of its own, with the number it moved.

**The wire before any change.** The edge serves everything text-shaped as Brotli, which is why
server-side compression is not on the list below: the script is 430,734 bytes built and 126,267 on the
wire, the stylesheet 48,924 and 8,256, the listing page of a hundred vehicles 105,385 and 13,684, the
page itself 4,940 and 1,783. The edge also keeps the hashed bundle files (`Cache-Status: hit` on the
stylesheet, `stored` on the script the first time after a roll), so a returning visitor's bundle never
reaches Azure, and it forwards every API request (`fwd=miss`), which is what `no-cache` asks of it.

**A first visit, desktop Chromium, cold cache, 1.0.0.139.** Two rounds on the Azure SQL site after the
first warmed the edge: first contentful paint 1,092 and 1,016 ms, largest contentful paint 1,792 and
1,720 ms, the inventory drawn at 1,926 and 1,878 ms, 28 to 29 requests and 568 to 589 KB on the wire.
The Cosmos DB site read the same shape: 1,012 and 956 ms to first paint, 1,504 and 1,484 ms to the
largest. Seventeen of the requests were card photographs, 450 KB of the total; five went to Google for
the font, one stylesheet and four files; one was the script.

**Lighthouse, throttled phone profile, 1.0.0.139:** performance 76, first contentful paint 2.8 s,
largest contentful paint 2.9 s, total blocking time 290 ms, speed index 2.9 s, layout shift 0.175. One
render-blocking resource on the page, the Google Fonts stylesheet, at an estimated 852 ms, and 67 KiB
of script the landing page loads and never runs.

**An idle minute, signed out, 1.0.0.139:** the inventory page asked for the listing and the filter
values four times each and fetched five photographs it had not shown before, 13 requests and 188 KB;
a vehicle page, whose listing keeps refreshing behind it, four to eight requests.

| Version | What changed | Before | After, measured live |
| --- | --- | --- | --- |
| 1.0.0.140 | The type is the site's own: four Poppins files under `/assets` in place of a Google stylesheet and four files from two third-party hosts ([The palette](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-016-palette.md), addendum) | 5 requests to 2 font hosts on every cold visit; the one render-blocking resource on the page, 852 ms on a throttled phone. Lighthouse: first contentful paint 2.8 s, largest 2.9 s, speed index 2.9 s. Desktop, cold cache, two rounds: first paint 1,092 and 1,016 ms | 0 third-party requests, 0 render-blocking resources. Lighthouse: first contentful paint 1.6 s, largest 1.8 s, speed index 2.4 s (`lane0917-966-measure140.log`). Desktop: 696 and 936 ms. Total blocking time read 580 ms against 290 before, on a machine that was also building; the type does not run script, and the next row is the one that removes some |
| 1.0.0.141 | The document renderer, `marked` and highlight.js with sixteen grammars, moves out of the page's script into a chunk fetched with the first document a reader opens ([Code that reads like code](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-074-code-that-reads-like-code.md), addendum) | one script, 430,734 bytes built and 126,267 on the wire; 67 KiB of it unused on the landing page by Lighthouse; Lighthouse 76, largest contentful paint 1.8 s, total blocking time 580 ms | the page's script 314,334 bytes built and 89,786 on the wire, 36,481 fewer (29 per cent); the renderer 117,430 bytes in a chunk of its own, fetched once with the first document. Lighthouse 84, largest contentful paint 1.6 s, total blocking time 320 ms, unused script 40 KiB (`lane0917-981-measure141.log`). Desktop, cold cache: no long task on either round, where every earlier read had one |
| 1.0.0.142 | The filter values are built once with the catalogue and asked for once per page: `/api/facets` walked all hundred thousand vehicles four times per request and the page re-asked on every listing refresh ([The search index](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-025-search-index.md), addendum) | server-side p50 29 and 34 ms on the two freshly rolled 1.0.0.140 containers after twenty reads, 66 and 28 on 141; an idle minute on the inventory page asked for the facets four times and a vehicle page twice to four times | server-side p50 0 ms on both fresh 1.0.0.142 containers, max 6 and 2 ms (`after-ship1.0.0.142-t1.log`); the idle minute asks for the facets 0 times on either page, 7 requests on the inventory page against 13, 3 to 4 on a vehicle page against 4 to 8 (`lane0917-974-measure142.log`); Lighthouse 83, speed index 2.1 s |
| 1.0.0.143 | A WebP copy of every photograph at both widths, offered first through a `picture` element, the JPEG pair as the fallback ([Responsive photos](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-036-responsive-photos.md), addendum) | seventeen card photographs at about 450 KB of a desktop first visit's 570; Lighthouse phone profile 1,565 KiB total at 1.0.0.142, 2,079 at 1.0.0.140, most of it the 1280 copies a dense screen takes; the fifty 1280 JPEGs 14,361 KB, the fifty 480 JPEGs 1,362 KB | the WebP sets 8,131 KB at 1280 (43 per cent under) and 1,281 KB at 480 (six per cent under), measured by the resizer on the machine. Live: every card photograph a cold visit fetched was the WebP copy, 41 of them across six rounds and no JPEG; Lighthouse phone profile 1,145 KiB against 1,565 (27 per cent less), 85, total blocking time 270 ms (`lane0917-977-measure143.log`) |

Read as one line from 1.0.0.139 to 1.0.0.143, all on Lighthouse's throttled phone profile against the
live site, the same tool from the same machine before and after: performance 76 to 85, first contentful
paint 2.8 s to 1.6 s, largest contentful paint 2.9 s to 1.6 s, one render-blocking resource to none,
1,628 KiB to 1,145 KiB, and the page's script from 126,267 to 89,635 bytes on the wire. What did not
move, and is said here so the table does not imply it did: layout shift stayed at 0.175 through every
version, which is the font swap and the card grid, and the honest answer to it, `font-display: optional`,
costs a first visit the face entirely (ADR: The palette, addendum). Nothing above added a resource, a
tier or an edge feature to the bill; the whole of it is configuration and files the repository already
knew how to make.

The four faces, declared once and hashed by the build like every other bundle file:

```live path=src/styles/fonts.css region=*
```

## Measured again on 5 October, at 1.0.3.74

Steve asked for the site to be faster and to wake up more evenly. Before anything changed, the same two tools as on 17 September were run from his machine in Missouri against the live sites: Lighthouse 12.8.2, three runs a form factor on the Azure SQL site and one on the Azure Cosmos DB site, with the median shown, and a probe that reads each response's time to first byte and its cache status (`leadspeed-measure-before-10374.log`).

| Landing page, 1.0.3.74 | Score | First paint | Largest paint | Speed index | Blocking | Layout shift | Weight |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Azure SQL site, phone | 89 | 2.2 s | 2.4 s | 5.0 s | 182 ms | 0 | 221 KiB |
| Azure SQL site, desktop | 99 | 0.5 s | 0.6 s | 1.4 s | 0 ms | 0 | 221 KiB |
| Azure Cosmos DB site, phone | 93 | 1.7 s | 1.9 s | 5.5 s | 120 ms | 0 | 221 KiB |
| Azure Cosmos DB site, desktop | 98 | 0.5 s | 0.6 s | 1.5 s | 2 ms | 0 | 221 KiB |

**Layout shift is zero now.** The 0.175 above held through 1.0.0.143. Since then the landing page was rebuilt and the face became IBM Plex Sans, and on 1.0.3.74 Lighthouse reads 0.000 on every run, on both sites, on a phone and on a desk. The fallback-font overrides planned for the shift (`size-adjust` and the ascent and descent overrides) were left out: there is no shift for them to remove, so they would be code with nothing to show for it.

**The phone's speed index is the number left high.** The largest paint lands at about 2.4 s and the page reads as finished at about 5 s. The landing figures fill in after the first paint, from readings the API serves; which reading sets the 5 s has not been measured yet, and it is the next place to look.

**The script at the edge.** The hashed script was asked for ten times in a row from Missouri on each site, with no header asking for a fresh copy. Six of the twenty were edge hits, at 0.11 to 0.15 s to first byte for five of them; the other fourteen were misses at 0.19 to 0.46 s, each one marked `stored`. The headers are right: public, a year, immutable. The edge keeps a cache per node and several per location, so a site with little traffic lands on a cold one most of the time ([Cache headers](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-015-cache-headers.md), the addenda of 28 September and 5 October). A miss costs a first visit about a quarter of a second on the script, once; the browser then keeps the file for a year. Serving the built files from the edge itself would end the misses, and it is priced in [Edge deploy economics](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-007-edge-economics.md), the addendum of 5 October. It is Steve's call, because it raises the bill.

**The listing through the edge.** The first hundred vehicles came back through the edge in 0.34 to 2.43 s to first byte across twenty reads, and straight from Azure in 0.32 to 0.56 s, with the whole answer, about 106 KB of JSON, in by 0.54 to 0.80 s. The edge forwards every API read and receives that answer uncompressed, then compresses it to about 16 KB for the browser. The versions after this one take that hop apart.

**After 1.0.3.77, the hop carries 15 KB and the time did not move.** Read from Missouri twenty minutes after the roll, the listing straight from Azure came back as about 15.5 KB of gzip in 0.62 to 0.95 s, against 106 KB in 0.54 to 0.80 s before. The bytes fell by 85 per cent. The time stayed put because most of it is the server building the page: 406 ms at the median on the Azure SQL site and 692 ms on the Azure Cosmos DB site, by each container's own request ring. Timed on Steve's machine against the same build, compression costs under 10 ms a page: a page took 79 to 100 ms plain and 87 to 96 ms as Brotli. In the first minutes after a roll the same median read 3.9 s, which is the slow wake-up Steve noticed (`leadspeed-measure-probe-10377-warm.log`, `leadspeed-bench-compress.log`).

**Where the server's time went, and 1.0.3.80.** Timed on Steve's machine by the shape of the query, fifteen reads each: the default listing took 90 to 100 ms, the same page sorted by price 35 to 45 ms, and one make with one row 25 to 30 ms. The default order was the cost: every request worked out all hundred thousand auction windows and sorted every match by them to keep a hundred. 1.0.3.80 builds that order once a day and reads the page off it ([The search index](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-025-search-index.md), the addendum on the schedule order). On the same machine and build the default listing went from 90 to 100 ms to 25 to 34 ms (`leadspeed-bench-listing.log`, `leadspeed-bench-listing-item5.log`); the live numbers are in the next section.

## Read live after 1.0.3.80, at the same age as the reading before it

A container's age moved these numbers more than any of the versions did. Every probe taken within about twenty-five minutes of a roll caught listing reads of several seconds: up to 8 s through the edge on the Azure SQL site 22 minutes after the 1.0.3.80 roll, and a longest single read of 40 s in the Azure Cosmos DB site's own ring. After 1.0.3.77 the longest was 70 s, so the wake-up was there before the edge copy and the schedule order existed. The before and after below are therefore both read about 73 minutes after a roll: 1.0.3.78, which already had compression, at 4,375 s of uptime, and 1.0.3.80 at 4,438 s (`leadspeed-measure-probe-10379-warm.log`, `leadspeed-measure-probe-10380-73min.log`).

| The listing through the edge, ten reads in a row from Missouri | 1.0.3.78 | 1.0.3.80 |
| --- | --- | --- |
| Azure SQL site, answered from the edge's copy | 0 of 10 | 4 of 10, at 0.11 to 0.13 s |
| Azure SQL site, forwarded to Azure | 0.22 to 0.28 s | 0.24 to 0.30 s |
| Azure Cosmos DB site, answered from the edge's copy | 0 of 10 | 2 of 10, at 0.10 to 0.12 s |
| Azure Cosmos DB site, forwarded to Azure | 0.24 to 0.83 s | 0.27 to 0.76 s |

**What moved.** The edge copy from 1.0.3.79 is the change a visitor can feel. When the edge holds the listing, the first hundred vehicles arrive in about a tenth of a second.

**What did not.** A read the edge forwards to Azure takes what it took before, about a quarter of a second. The schedule order cut the server's own work by two thirds on the build machine, but on a warm container that work was already a small part of a forwarded read. The rest is the trip from Missouri to the edge and on to Azure: the same read sent straight to Azure took 0.27 to 0.33 s on both versions. Each container's ring gave a server median of 174 ms before and 131 ms after on Azure SQL, and 97 ms before and 238 ms after on Azure Cosmos DB. The ring holds the last five hundred requests of every kind, wake-up reads included, so those two pairs are counted as neither a gain nor a loss.

**Against where the day started.** At 1.0.3.74 the same ten reads through the edge took 0.34 to 1.53 s on the Azure SQL site and 0.36 to 2.43 s on the Azure Cosmos DB site, every one forwarded, with 106 KB crossing from Azure to the edge. On 1.0.3.80 they took 0.11 to 0.30 s and 0.10 to 0.76 s, with 16 KB crossing. That earlier reading did not record the container's age, so part of the difference may be warm-up.

| Landing page, 1.0.3.80 | Score | First paint | Largest paint | Speed index | Blocking | Layout shift | Weight |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Azure SQL site, phone | 91 | 1.9 s | 2.2 s | 5.8 s | 135 ms | 0 | 221 KiB |
| Azure SQL site, desktop | 99 | 0.5 s | 0.5 s | 1.2 s | 0 ms | 0 | 220 KiB |
| Azure Cosmos DB site, phone | 92 | 1.9 s | 2.3 s | 5.8 s | 35 ms | 0 | 220 KiB |
| Azure Cosmos DB site, desktop | 97 | 0.7 s | 0.8 s | 1.5 s | 0 ms | 0 | 221 KiB |

Lighthouse 12.8.2 as before: three runs a form factor on the Azure SQL site and one on the Azure Cosmos DB site, read 17 minutes after the roll (`leadspeed-measure-after-10380.log`). Against 1.0.3.74 the scores moved by one or two points, inside the spread of three runs on a phone (87 to 92). Layout shift stayed at zero. The phone's speed index read 5.8 s against 5.0 s, and which reading sets it is still unmeasured.

**The wake-up is the open problem.** For twenty minutes or more after each roll, single listing reads take seconds, and none of the five versions changed that. What the container spends those minutes on has not been measured, and it is the next thing to look at.

## How the listing is compressed, and why only the listing

**The trip.** A visitor's browser asks the edge, Netlify, for the listing. The edge forwards the request to the container on Azure and passes the answer back. The edge has always compressed what it hands the browser, so the browser never saw the full size. The waste was on the hop between them: the edge asked Azure for the answer plainly, and a page of a hundred vehicles crossed as about 106 KB of JSON.

**What changed.** The container now compresses two answers itself, the listing (`/api/vehicles`) and the filter values (`/api/facets`), as Brotli or gzip, whichever the caller asks for. The same page crosses as about 15 KB, 85 per cent less. ASP.NET Core's response compression does the work. It is registered once, in [`ApiRegistration.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/ApiRegistration.cs), read here from the running build with its comments:

```live path=api/TheYard.Api/Composition/ApiRegistration.cs region=compression
```

It is switched on only for a GET of those two addresses, in [`RequestPipeline.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/RequestPipeline.cs):

```live path=api/TheYard.Api/Composition/RequestPipeline.cs region=compression
```

**Why only those two.** Compressing an HTTPS response can leak a secret through its size. The attack is called BREACH. It needs a secret, such as a session token, and text the attacker chooses in the same compressed body. By sending many requests and watching the length shrink, the attacker guesses the secret a character at a time. That is why ASP.NET Core leaves HTTPS compression off unless it is asked for. The listing and the filter values are the same public data for every visitor and carry no secret in their bodies; a session travels in the Cookie header, which this compression never touches. Bids, accounts and the admin endpoints are sent as they are.

**Why the optimal level.** The fastest level was tried first and sent 44 KB. The serializer writes the answer in many small pieces and each piece is flushed through the compressor, and at the fastest level each flush throws away most of the gain. The optimal level, which for Brotli is quality 4, brings the page to about 15 KB. Timed on the build machine, a page took 79 to 100 ms plain and 87 to 96 ms as Brotli, so the cost is lost in the noise of building the page.

**What it did not do.** It cut the bytes and left the time alone. Most of a listing read was the server building the page, and on a warm container the rest is the trip from Missouri to the edge and on to Azure. The 3.9 s median read just after the roll was the container waking up, and the bench above rules out the compression. The changes a visitor can feel came next: the edge keeping its own copy for a few seconds, and the schedule order behind the default listing.

[`CatalogueReads.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/CatalogueReads.cs) names the two addresses. The decision is recorded in [Cache headers](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-015-cache-headers.md), the addendum on compression.

## What it cost to keep it honest

Measuring is not free either, and the bill is small enough to print: the whole twenty-round measurement
session cost the document store **1,096 request units**, which is about a twenty-seventh of a cent on
serverless pricing.

On the edge, eleven production deploys had each redeployed the edge too and eaten **165 of the 300 free
credits in a month at 15 each**, while actually serving the site cost almost nothing. Application pushes no longer redeploy the
edge, so they cost **zero credits**. ([Edge economics](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-007-edge-economics.md))

## What the free tier taught on the fourteenth

The relational store started on Azure SQL Database's free offer: 100,000 vCore-seconds of serverless
compute a month, auto-pause after an idle hour, and when the amount is spent the database pauses until the
first of the next month. That is fifty-five awake hours at the 0.5 vCore floor, and a database is awake
whenever anything writes it. The site activity collector, which had written a batch every five seconds
since 13 September, kept it awake around the clock, and at 06:40 UTC on 14 September Azure paused it with
its own sentence in the exception (error 42119). The site kept serving from its files, accounts and bids
went with the database, and the activity card answered 500 on both sites because the SQL half of its
read threw first.

Two things changed, both measured before they were decided
([The SQL Server backend](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-039-sql-server-backend.md),
[Site activity](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-071-site-activity.md)).
The activity rows are kept in Azure Cosmos DB by both sites, each row naming the store that served it,
so nothing writes the relational store every five seconds and a paused database cannot take the card
down. And the site points at a Basic database, 5 DTU, on the same server: a fixed **$4.90 a month**, no
pause, no allowance to run out. The serverless one resumes on the first of October and nothing points at
it. The lesson is the one the table above already implied: a free tier is a budget, and a background
writer spends it whether or not a visitor is there.

## Check any of it yourself

- Open the **Admin** tab on either site: live health checks per store, the paired-round comparison card,
  Azure's own view of the site and the plan it shares, and every store operation the application has made
  with how long it took.
- `POST /api/admin/proof` starts a fresh run; `GET /api/admin/proof` reads the one in progress or the last
  one finished.
- Every figure above links to the record that holds the method, and the code samples on this page and
  inside those records are read from the running build rather than pasted, so a page cannot drift from the
  code it describes.

## Files

- [`api/TheYard.Api/Proof.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Proof.cs) and [`api/TheYard.Api/ProofRunner.Rounds.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/ProofRunner.Rounds.cs): the runner, and its paired rounds, alternating which store goes first.
- [`api/TheYard.Infrastructure/YardDatabase.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/YardDatabase.cs): `PingAsync`, the round trip to the relational store the proof measures.
- [`src/components/admin/BackendsCard/BackendsCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/admin/BackendsCard/BackendsCard.tsx) and [`src/components/admin/ProofCard/ProofCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/admin/ProofCard/ProofCard.tsx): the comparison card and the proof card the numbers above are read from.
- [`infra/cosmos`](https://github.com/SteveStout/TheYard/tree/main/infra/cosmos): the container definitions, indexing policy and partition key included.
- [`infra/appservice.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/appservice.bicep): the plan and the two sites, 1 vCPU and 1.75 GB between them.
