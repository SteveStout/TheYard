# Web overview

The [Infrastructure overview](https://github.com/SteveStout/TheYard/blob/main/docs/INFRASTRUCTURE-OVERVIEW.md) is the machines a
request crosses. This page is what those machines send a browser, and the order a first visit
loads it in. The [Performance overview](https://github.com/SteveStout/TheYard/blob/main/docs/PERFORMANCE.md) holds what each change
to the page moved; this one is the page as it stands at 1.0.0.144.

## What the page is made of

| Piece | What it is | On the wire | How long a browser may keep it |
| --- | --- | --- | --- |
| The document | One HTML page, the shell of a React 19 application built by Vite 8 in TypeScript | about 2.1 KB | never; `no-cache`, so a new version is seen on the next visit |
| The script | One bundle, named by a hash of its contents | about 90 KB (314,334 bytes built) | a year, `immutable`; a new build is a new name |
| The document renderer | `marked` and highlight.js, a chunk of its own since 1.0.0.141 | 117,430 bytes built, fetched with the first document a reader opens and never on the inventory page | a year |
| The stylesheet | One file, hashed | about 8.5 KB | a year |
| The type | Four Poppins files served from `/assets` since 1.0.0.140, in place of Google Fonts | about 8 KB each | a year |
| The photographs | A WebP copy at 480 and 1280 wide offered first through a `picture` element, the JPEG pair as the fallback | 15 to 37 KB each at 480 | a day |
| The data | JSON from `/api`, the listing of 100 vehicles the largest at about 13.7 KB | | never; every API read is forwarded to the container |

The edge serves everything text-shaped as Brotli, so the wire sizes above are compressed sizes. The
cache rules are one decision, in
[Cache headers](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-015-cache-headers.md).

## The order a first visit loads in

Read on 19 September at 10:58 CDT against the live 1.0.0.144 on both sites by
[`scripts/load-order.cjs`](https://github.com/SteveStout/TheYard/blob/main/scripts/load-order.cjs): the repository's own Playwright
Chromium, headless, a desktop viewport, a fresh browser context per round so every visit is cold,
three rounds a site (the log is `lane0919-985-loadorder.log`). Times are milliseconds from the moment
the browser asked for the page.

The four waves were the same on every round of both sites:

1. **The page itself.** One HTML document, about 2.1 KB on the wire. Nothing else can start until it
   arrives, so the time to its first byte, 272 to 302 ms on five of six rounds, is the floor under
   everything below.
2. **What the HTML names: one script and one stylesheet**, asked for within 10 ms of the document
   landing, both from the site's own domain over the same HTTP/2 connection. The stylesheet, 8.5 KB,
   is the only thing the first paint waits for: on all six rounds the first paint came 5 to 9 ms
   after the stylesheet finished.
3. **What those two name.** The stylesheet asks for the four Poppins files, about 8 KB each at the
   highest priority; the script asks for six API reads at once: stores, bids, filter values,
   version, who is signed in, and the listing of 100 vehicles. The first contentful paint lands
   inside this wave on every round.
4. **The photographs.** The cards cannot ask for a photograph until the listing answers, so 15 or
   16 WebP images go out together at low priority, inside 3 ms of each other and 70 to 141 ms after
   `/api/vehicles` finishes; on two rounds the sixteenth came later, at 1,890 and 3,516 ms. The
   largest contentful paint lands while they arrive, on every round.

Every request on a cold visit went to the site's own domain over HTTP/2 and answered 200. No
third-party host appears in any of the six rounds.

## The marks, all six rounds

| Site | Round | First byte | First paint | First contentful paint | Largest contentful paint | Requests |
| --- | --- | --- | --- | --- | --- | --- |
| Azure SQL | 1 | 3,332 ms | 3,436 ms | 3,664 ms | 4,988 ms | 28 |
| Azure SQL | 2 | 301 ms | 632 ms | 868 ms | 1,568 ms | 29 |
| Azure SQL | 3 | 281 ms | 1,168 ms | 1,268 ms | 1,956 ms | 29 |
| Cosmos DB | 1 | 302 ms | 600 ms | 880 ms | 1,472 ms | 29 |
| Cosmos DB | 2 | 278 ms | 332 ms | 528 ms | 1,824 ms | 29 |
| Cosmos DB | 3 | 272 ms | 608 ms | 852 ms | 1,372 ms | 28 |

The first round on the Azure SQL site waited 3,332 ms for the document's first byte, and every later
mark on that round carries those three seconds. The other five rounds read 272 to 302 ms. One more
round a site, read at 11:19 CDT the same day from the script's place in this repository, had the same
shape: 3,240 ms on the Azure SQL site and 297 ms on the Cosmos DB site. It is reported as measured;
two samples do not name a cause.

## One round in full: the Cosmos DB site, round 2

Each line is one request in the order it was sent: when it was sent, when its first byte and its
last arrived, what it is, the priority the browser gave it, its bytes on the wire, and what asked
for it.

```
  round 2: ttfb 278 ms, first paint 332 ms, FCP 528 ms, DOMContentLoaded 440 ms, load 441 ms, LCP 1824 ms; 29 requests
     1 sent     0 ms, first byte   280, done   279 | Document   VeryHigh    2143 B 200 h2 | /?nocache=N | by other
     2 sent   285 ms, first byte   398, done   393 | Script     High       89823 B 200 h2 | /assets/index-P_kVD16D.js | by parser /?nocache=N
     3 sent   285 ms, first byte   325, done   324 | Stylesheet VeryHigh    8490 B 200 h2 | /assets/index-B9hryYpi.css | by parser /?nocache=N
     4 sent   467 ms, first byte  1280, done  1279 | Fetch      High         249 B 200 h2 | /api/stores | by script /assets/index-P_kVD16D.js
     5 sent   469 ms, first byte  1313, done  1328 | Fetch      High          67 B 200 h2 | /api/bids | by script /assets/index-P_kVD16D.js
     6 sent   469 ms, first byte  1307, done  1305 | Fetch      High         299 B 200 h2 | /api/facets | by script /assets/index-P_kVD16D.js
     7 sent   469 ms, first byte  1306, done  1305 | Fetch      High         105 B 200 h2 | /api/version | by script /assets/index-P_kVD16D.js
     8 sent   470 ms, first byte  1307, done  1306 | Fetch      High         106 B 200 h2 | /api/auth/me | by script /assets/index-P_kVD16D.js
     9 sent   473 ms, first byte  1376, done  1508 | Fetch      High       13753 B 200 h2 | /api/vehicles | by script /assets/index-P_kVD16D.js
    10 sent   479 ms, first byte   551, done   547 | Font       VeryHigh    8084 B 200 h2 | /assets/poppins-latin-600-zEkxB9Mr.woff2 | by parser /assets/index-B9hryYpi.css
    11 sent   479 ms, first byte   549, done   546 | Font       VeryHigh    7909 B 200 h2 | /assets/poppins-latin-700-Qrb0O0WB.woff2 | by parser /assets/index-B9hryYpi.css
    12 sent   479 ms, first byte   558, done   556 | Font       VeryHigh    7832 B 200 h2 | /assets/poppins-latin-500-C8OXljZJ.woff2 | by parser /assets/index-B9hryYpi.css
    13 sent   507 ms, first byte   550, done   547 | Font       VeryHigh    7967 B 200 h2 | /assets/poppins-latin-400-cpxAROuN.woff2 | by parser /assets/index-B9hryYpi.css
    14 sent  1578 ms, first byte  1798, done  1797 | Image      Low        16895 B 200 h2 | /api/images/sedan-10-480.webp | by other
    15 sent  1579 ms, first byte  1682, done  1726 | Image      Low        25961 B 200 h2 | /api/images/suv-04-480.webp | by other
    16 sent  1579 ms, first byte  1609, done  1656 | Image      Low        21373 B 200 h2 | /api/images/sedan-05-480.webp | by other
    17 sent  1579 ms, first byte  1727, done  1750 | Image      Low        25750 B 200 h2 | /api/images/suv-03-480.webp | by other
    18 sent  1579 ms, first byte  1658, done  1657 | Image      Low        26552 B 200 h2 | /api/images/suv-06-480.webp | by other
    19 sent  1579 ms, first byte  1659, done  1657 | Image      Low        15357 B 200 h2 | /api/images/suv-07-480.webp | by other
    20 sent  1579 ms, first byte  1703, done  1749 | Image      Low        26698 B 200 h2 | /api/images/suv-08-480.webp | by other
    21 sent  1579 ms, first byte  1751, done  1821 | Image      Low        18453 B 200 h2 | /api/images/truck-03-480.webp | by other
    22 sent  1579 ms, first byte  1798, done  1843 | Image      Low        15863 B 200 h2 | /api/images/sedan-06-480.webp | by other
    23 sent  1580 ms, first byte  1681, done  1680 | Image      Low        26691 B 200 h2 | /api/images/suv-02-480.webp | by other
    24 sent  1580 ms, first byte  1659, done  1679 | Image      Low        24212 B 200 h2 | /api/images/truck-04-480.webp | by other
    25 sent  1580 ms, first byte  1774, done  1820 | Image      Low        37165 B 200 h2 | /api/images/sedan-04-480.webp | by other
    26 sent  1580 ms, first byte  1845, done  1844 | Image      Low        23585 B 200 h2 | /api/images/sedan-07-480.webp | by other
    27 sent  1580 ms, first byte  1751, done  1820 | Image      Low        15984 B 200 h2 | /api/images/sedan-03-480.webp | by other
    28 sent  1580 ms, first byte  1774, done  1821 | Image      Low        20352 B 200 h2 | /api/images/sedan-09-480.webp | by other
    29 sent  3516 ms, first byte  3612, done  3635 | Image      Low        24670 B 200 h2 | /api/images/hatchback-01-480.webp | by other
```

## What the order says

- **The first paint waits on two things, the document and an 8.5 KB stylesheet, both from the
  site's own domain.** Until 1.0.0.140 it also waited on a stylesheet from Google Fonts, 852 ms on a
  throttled phone by Lighthouse, and that was the one render-blocking resource on the page. Serving
  the four font files from `/assets` was that version's whole change to the page, and on
  Lighthouse's throttled phone profile against the live site the first contentful paint went from
  2.8 s to 1.6 s, while a cold visit's requests to third-party hosts went from five to none
  ([The palette](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-016-palette.md), addendum; the row with both readings is
  on the [Performance overview](https://github.com/SteveStout/TheYard/blob/main/docs/PERFORMANCE.md)).
- **The script does not block the paint and the fonts do not block the text.** The script is a
  module, which a browser defers, and the type swaps in when it lands, which is the 0.175 layout
  shift the Performance overview names and leaves alone.
- **The photographs are last on purpose.** They are the largest bytes on the page and nothing a
  visitor reads depends on them
  ([Responsive photos](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-036-responsive-photos.md)).

## Check any of it yourself

`node scripts/load-order.cjs <log path> <label> <site url> [rounds]` from a checkout with the
browser suite installed. It reads and never writes: a cold visit, the requests in the order they
were sent, and the paint marks between them.

## Files

- [`scripts/load-order.cjs`](https://github.com/SteveStout/TheYard/blob/main/scripts/load-order.cjs): the script that produced every number in the load order above.
- [`index.html`](https://github.com/SteveStout/TheYard/blob/main/index.html): the document, and the one script it names.
- [`src/styles/fonts.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/fonts.css): the four faces, declared once and hashed by the build.
- [`src/components/VehicleImage.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/VehicleImage.tsx): the `picture` element that offers the WebP pair first.
- [`netlify.toml`](https://github.com/SteveStout/TheYard/blob/main/netlify.toml) and [`edge/_redirects`](https://github.com/SteveStout/TheYard/blob/main/edge/_redirects): the edge that compresses and forwards.
