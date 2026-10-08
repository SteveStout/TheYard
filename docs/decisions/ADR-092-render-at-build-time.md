# ADR: The landing page rendered at build time, server rendering as the goal

Status: accepted, 2026-10-07. The landing page's API reads start from the page's head in 1.0.3.87, and the landing page is drawn into the HTML by the frontend build in 1.0.3.88. Full server rendering is the goal this record writes down; it is not built, and the one reading taken for it is below.

## In plain words

Until now a visitor's browser drew every page of the site itself: the HTML it receives is an empty frame, and nothing appears until the 99 KB script has arrived and run. This record decides three things. The API reads the landing page needs start from the page's head instead of waiting for that script. The landing page is drawn to HTML when the site is built, so the browser can show it before any script runs, and React then takes over what is already there. And the long-term goal, written down here so it is not lost, is full server rendering: every page drawn on each request by a rendering service of its own, beside the API and apart from it, with the API staying a service that answers in JSON and knows nothing about pages.

What that is worth: a visitor on a phone sees the landing page sooner, a developer can read why the page is built this way and rerun the numbers that decided it, and the organization keeps the API free of user interface code, so either side can change without the other.

## Context

Steve, 7 October: "I want the pages very heavy API driven with API and onion Arcitecture with the ui seperateted from the server", and "I want all of this documented with our future goal to do A". A is full server rendering.

The Performance overview left one question open after 1.0.3.80: the phone's speed index read 5.8 s, and which reading set it had not been measured. That 5.8 s was read 17 minutes after a roll, while both containers were still warming. It was measured again on 7 October against 1.0.3.86, with both containers 870 minutes old, from Steve's machine in Missouri: Lighthouse 12.8.2 on its phone preset, three runs a site, each container's uptime read before and after every run (`renderlane-si-before-10386.log`).

| Landing page, 1.0.3.86, phone | Run 1 | Run 2 | Run 3 | Median |
| --- | --- | --- | --- | --- |
| Azure SQL site, speed index | 5.58 s | 4.06 s | 4.89 s | 4.89 s |
| Azure Cosmos DB site, speed index | 3.67 s | 3.71 s | 11.91 s | 3.71 s |

The Cosmos DB site's third run waited 5.8 s for the page's own HTML, so everything after it was late by that much; it is reported as measured.

**No request sets the speed index.** On every run the landing page appears in one frame, so the speed index is the time of that frame. Every API read had finished 0.4 to 1.4 s before it, `/api/health` the last of them. Between the renderer's last work and that frame, the browser's GPU process was busy for the whole gap, 0.9 to 2.5 s, under Lighthouse's trace (`renderlane-si2-before2-10386.log`). Why it took that long under the trace was not measured. The same Chrome, opened by a script without Lighthouse, drew that frame 0.1 to 0.3 s after the text arrived. Lighthouse's phone speed index is a simulation: it multiplies the speed index it saw on the machine running it by about 1.4 and adds a term from the page's layout, so the time that frame takes on the measuring machine carries straight into the figure.

**What a page drawn at build time would give**, measured before building it: the same Chrome, at Lighthouse's phone size, five cold rounds each, with the landing page's markup put inside the HTML and the script left out (`renderlane-paint-ab2-10386.log`).

| First contentful paint, Azure SQL site, median of five | As served | Markup already in the HTML |
| --- | --- | --- |
| With the frosted glass | 804 ms | 456 ms |
| With every backdrop filter off | 620 ms | not read |

The glass costs 70 to 180 ms of that first frame on this machine. It is the site's look and it stays; this record does not touch it.

## Decision

**1. The landing page's API reads start from the head (1.0.3.87).** A short script at the top of `index.html`, before the stylesheet so nothing waits on it, adds a preload hint for each read: `/api/stores`, `/api/auth/me`, `/api/bids` and `/api/version`, which every view makes on its first load, and `/api/health` and `/api/tests/summary`, which only the landing page makes, added only when the address carries no query. Each hint matches its request exactly, or the browser asks twice: `as="fetch"` with `crossorigin="anonymous"` is a request in `cors` mode with `same-origin` credentials and no extra headers, which is what each of those six `fetch` calls sends. `tests/e2e/preload.spec.ts` holds it: on the built page every hinted address is asked for once, and on any page the browser reports no hint it could not use.

**2. The landing page is rendered to HTML in the frontend build (1.0.3.88).** The Vite build renders the landing page with React's own server renderer into `dist/index.html`, and `mount.tsx` uses `hydrateRoot` where the page arrived drawn and `createRoot` where it did not. The .NET host serves the built files as it does now and knows nothing about them: no rendering, no page templates and no Node process in the API's container.

**3. Full server rendering is the goal, and is not built.** A rendering service of its own, in Node, would draw any page on request and ask the API for its data like any other client. It would be a third site on the plan or a container of its own, never code inside the API.

**The reading that decides whether it fits on B1.** The plan is one B1, 1.75 GB and one core shared by both sites (`infra/main.bicep`). Each container reports a limit of 1,183 MB. Over the hour to 08:19 CDT on 7 October, read from each container's own samples every 15 seconds (`renderlane-memory-10386`): the Azure SQL site's working set ran 331 to 465 MB, median 419, and the Azure Cosmos DB site's 360 to 481 MB, median 411. Both peaks together are 946 MB, which leaves about 846 MB of the plan's 1,792 MB before the operating system's own share, which was not read. By memory, a rendering service fits. The processor was not measured for it, and it is the next reading: the one core is already where the listing's slow tail comes from (Performance overview, the reading of 2 October), and a renderer would draw on the same core for every page it serves.

## What was rejected

- **Rendering inside the .NET host**, by a Node process in the API's container or by templates in the host. It would put page knowledge in the server, which is the separation Steve asked to keep.
- **A static hint for every read on every page.** A page that opens on the inventory or the Admin tab never reads `/api/health` or `/api/tests/summary` itself, so the hints would be requests nobody uses and a warning in every reader's console. Those two are hinted only where the landing page is what opens.

## Addendum, 2026-10-07 (1.0.3.90): the numbers after

Both parts shipped: the hints in 1.0.3.87 and the drawn landing page in 1.0.3.88. Read after the roll of 1.0.3.88 on both live domains: the bare address's HTML carries the drawn page, React takes it over with no hydration error at phone and desk sizes and keeps the node the build drew, no API address is asked for twice, and an address that opens another view never shows the drawing (`renderlane-proof-1.0.3.88.log`).

The same Lighthouse reading as above, three phone runs a site, with both containers 75 to 77 minutes old (`renderlane-si-after-10388.log`):

| Landing page, phone, median of three | Speed index before | after | Largest paint before | after | Blocking before | after |
| --- | --- | --- | --- | --- | --- | --- |
| Azure SQL site | 4.89 s | 4.21 s | 2.66 s | 1.92 s | 110 ms | 255 ms |
| Azure Cosmos DB site | 3.71 s | 3.31 s | 2.20 s | 1.97 s | 349 ms | 242 ms |

The speed index and the largest paint improved on both sites. Blocking time rose on the Azure SQL site: taking over a drawn page is main-thread work an empty one never needed. The HTML grew from about 10 KB to 113 KB, 13.8 KB compressed. The first frame under Lighthouse's trace still lands about three seconds after the page has loaded, with the browser's other threads idle, which is the trace's and not the page's; the Performance overview carries the reading.

The goal is unchanged: full server rendering, beside the API and apart from it. The memory reading above was taken from the containers, and it left about 846 MB of the plan unaccounted for. The plan's own reading, at one-minute grain over the ten days to 7 October, accounts for it: 92 per cent used at the median and 96 at the 99th percentile, and still 77 per cent with both containers at 233 MB, so the platform holds roughly 700 to 800 MB of its own. A rendering service of about the size of one of the sites does not fit on this B1 beside both of them; it needs a larger plan or a plan of its own, and that is the reading to price when the goal is taken up.

## Addendum, 2026-10-07 (1.0.3.94): what is already in place for server rendering, held by tests

Steve, 7 October: "make sure anything that is ready for React server is in place", and "all of this should be enforced with automated tests". What a rendering service will stand on, and the test that now keeps each from drifting:

- **Every component the landing page renders draws with no browser.** `src/app/drawLanding.test.ts` draws the whole tree in Node, where there is no window, no document and no storage, so a component that reads the browser on its first draw fails the unit tests rather than the build or the live site.
- **The API host draws no page and runs no JavaScript.** `ServerRenderingReadinessTests` fails on a Razor or single-page-app hosting package, a page template, a Node process or a page-drawing registration in the API project, and holds that every app route is answered with the built `index.html` as a file.
- **Readiness per store, for a service that must not take traffic early.** `/readyz?stores=all` (ADR: The order of the request pipeline), held by `RequestPipelineTests`.
- **The API's reads start from the page's head**, so a server-drawn page and a browser-drawn one ask for the same data the same way (`tests/e2e/preload.spec.ts`), and the takeover is free of hydration errors (`tests/e2e/drawn.spec.ts`).

What is not in place, said plainly: a rendering service, and room for one. The plan's memory says a third process of the sites' size does not fit on this B1 (the addendum above).

```live path=src/app/drawLanding.test.ts region=draws-without-a-browser
```

## Where it sits

Outside the rings: this is the frontend's own page and its build. The API's rings do not change, the host serves the built files without reading them, and the one rule added is held by a browser spec (Single responsibility: the server answers in data, the frontend draws).

## Files

- [`index.html`](https://github.com/SteveStout/TheYard/blob/main/index.html): the hints, in the region `api-preload`.
- [`tests/e2e/preload.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/preload.spec.ts): every hinted address asked for once, and no hint the browser could not use.
- [`src/components/landing/Landing/Landing.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/landing/Landing/Landing.tsx): the landing page and its two reads.
- [`src/app/mount.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/app/mount.tsx): where the site is drawn into the page.
- [`infra/main.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/main.bicep): the plan both sites share.
