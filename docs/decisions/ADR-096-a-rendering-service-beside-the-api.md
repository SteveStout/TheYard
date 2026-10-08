# ADR: A rendering service beside the API

Status: proposed, 2026-10-08. The rendering service is in the repository with its tests in 1.0.3.100 and is not running anywhere yet. It is measured alone in Docker on Steve's machine before anything is created in Azure, the plan's memory decides where it runs, and it goes live behind the edge after that; each of those steps adds its numbers here, and this record is accepted in the version that puts it in front of visitors.

## In plain words

Until now the site's pages were drawn by the visitor's browser. The HTML that arrived was the frame and, on the bare address only, the landing page the build drew once (ADR: The landing page rendered at build time). Every other address arrived empty, and the browser showed nothing of it until a 300 KB script had arrived, run, and asked the API for the data. This record adds a rendering service: a small Node program beside the API that draws any page on each request, with the data already in it, and sends the HTML. The browser paints that HTML at once, and React then takes it over instead of drawing it again.

What that is worth: a visitor sees the inventory, a vehicle or a document as soon as the HTML lands instead of after the script and two API round trips, a crawler or an AI tool that runs no script reads the real page, and the API stays what it is, a service that answers in JSON and draws nothing.

## Context

Steve, 8 October: "we have extra claude this week how hard would it be to set up react on a server", then "the code for B1 and we'll see the 13$ a month is nothing if needed, but my gut says we won't need it", "when we can in the future cloudflare", and "I want to learn this technology and make the site as responsive as possible."

ADR-092 left two things in place for this: every component the landing page uses draws without a browser, held by a test, and `mount.tsx` already takes over a drawn page with `hydrateRoot`. It also left the rule this record keeps: the renderer is never code inside the API (`ServerRenderingReadinessTests`).

## Decision

**1. One function behind any door.** `render/render.ts` exports `createRenderer`, which gives one function: `render(url, headers, apiOrigin)` returns a standard `Response`. It uses web streams, `fetch` and React's edge renderer (`react-dom/server.edge`), and no Node API, so the same function runs on App Service under Node today and in a Cloudflare Worker when the domain moves there. `render/server.mjs` is the App Service door: Node's own `http` module, about 150 lines, no framework. Hono or Express would add a package to keep current and nothing this door uses; the door's one job is to hand a request to `render` and stream the answer back.

```live path=render/render.ts region=the-renderer
```

**2. The page comes from the API, and the service draws only for the build it was made from.** The service reads `index.html` from the API it draws for, so the page always names the bundle that API serves, and it reads `/api/version` beside it. React in that bundle takes the markup over only if the service drew it from the same code, so while the API runs another build (the minutes of a roll) the service sends the API's page untouched and the browser draws it, as the site did before. Both reads are kept for fifteen seconds per site.

**3. A loader per view, all at once, under one deadline.** `render/loaders.ts` reads what an address needs before it is drawn: who is signed in, the build for the footer, the first page of the list and its filter options on any inventory or vehicle address, the vehicle a vehicle's address names, and the document a document's address names, rendered here with the same Markdown code the browser uses. The reads run together under a 2.5 second deadline. A read that fails or runs past it is left out, and that view draws its loading state, which the browser then fills as it always did. Admin and the account page draw in the browser behind the frame the service drew (`useInTheBrowser`).

**4. The first load travels in the page.** What the loaders read goes into the page as a JSON script beside `#root`, with every `<` written as its escape so no vehicle text can end the script early. `mount.tsx` reads it, and every hook that used to start empty starts from it: the address, the list, the vehicle, the account, the build and the clock. That is what makes the browser's first draw match the service's draw exactly.

**5. Same markup on both sides, or React throws the page away.** Four things differ between a server and a browser, and each has an answer:

- The clock: the service writes the time it drew at, and the browser's first draw counts down from that time, then ticks.
- The time zone: a server does not know the viewer's, so an auction's start and end stamps say UTC until the page is the browser's, then the viewer's zone. The stamp names its zone both times.
- The window's width: `useMediaQuery` answers "does not match" on the server and while the page is taken over, then the window's answer, so a vehicle's header countdown joins on a desk a moment after the page paints.
- The document window: drawn open on the server, so it paints at once, and opened again as a modal once the page is live, so the backdrop and Escape behave as before.

**6. The top of the page leaves at once.** The service sends everything above `#root` before any API read has answered, so the browser starts on the stylesheet, the font and the bundle while the reads are out. React's own stream follows, then the first load, then the rest of the page. On a stand-in API in this session's harness that first byte left in 3 ms with the reads still out; the measured numbers go below once the service runs in Docker and then live.

```live path=render/render.ts region=the-stream
```

**7. It reaches the API only.** The service reads what any browser can read, from the API's public addresses, forwarding the visitor's cookie and nothing else, with the self mark on its agent so the Site activity card counts the visit once. It names no store and opens no connection of its own; `render/render.test.ts` holds both.

**8. One service for both sites.** The service draws for each site by path: `/site/sql` and `/site/cosmos`, each read from its own site's API. One Node process for both keeps the third container on the plan as small as it can be.

## What happens when the service cannot draw

The service never answers 5xx for a page it could not draw. A failed read leaves that view to the browser. A component that throws while drawing leaves `#root` empty with no first load, and the browser draws the page from nothing, as it did before this record. The one answer that is not a page is an API that does not answer at all: a 503, which the API's own address would answer too.

When the service itself is down, its container restarting or rolling, App Service answers for it, and the edge has no way to try the API instead. Until the edge moves to a Cloudflare Worker, which can catch that answer and fetch the API's page in its place, the way round is the address the API still serves every page from: `/index.html` with any query draws in the browser as it always did. The decision about where the edge sends pages, and its test, is the version that does it.

## What was rejected

- **Drawing inside the .NET host**, by a Node process in its container or by templates in the host: ADR-092 rejected it, and nothing here changes why.
- **A framework (Next.js, Remix)**: it would own the routing, the build and the server, and this site's routing is the address bar's query, held by its own hooks and tests. The service is one function over the app that already exists.
- **The page built into the service's image**: the service would draw into a page naming a bundle the API might no longer serve after the next roll. It reads the API's page instead.
- **Streaming the data through Suspense**: React streams markup for a slow part of the page, but the data the browser needs to take that part over has to arrive in the page too, and React 19 has no stable way to send it without a framework. The deadline in decision 3 gives the same outcome for this site, the frame first and the slow view after, with the browser asking for what the service did not wait for.

## Addendum, 2026-10-08: the first push did not roll

1.0.3.100 passed its gate on both stores and was pushed at 12:03 CDT, and neither site rolled. Deploy #284 stopped at Build and push after 27 seconds, and Deploy Cosmos waited for an image that never came. The cause was this record's own test: `src/app/drawServer.test.ts` read its vehicles from `data/vehicles.json`, the build type-checks every file under `src/`, tests included, and the image's frontend stage copies `src/` and `public/` and nothing else. On a developer's machine and in CI, which hold the whole repository, it built. The same build on Steve's machine stopped at the same line, `TS2307: Cannot find module '../../data/vehicles.json'` (`ssrlane-render-docker.log`). In 1.0.3.101 the test writes its vehicles out, and `DockerBuildInputsTests` fails any file under `src/` that imports from outside what the stage copies. The sites served 1.0.3.99 throughout.

## Addendum, 2026-10-08: measured alone in Docker, before Azure

`render/Dockerfile` builds the service in two stages: the site's own sources built with the site's own config, then Node and that build alone, with no `node_modules`, no sources and no package manager. The image is 62.4 MB beside the API's 208.1 MB.

It was measured on Steve's machine at 13:10 to 13:15 CDT, against the 1.0.3.101 checkout (`ssrlane-render-docker.log`). The API ran in a container of its own with the scratch SQLite store, the service in another at `--cpus=1` and a 512 MB limit, both images built with the same stamp so the service drew for that API. Each view was asked thirty times, one after another:

| View | HTML, gzip on the wire | First byte, p50 / p95 | Drawn `#root`, p50 / p95 |
| --- | --- | --- | --- |
| Landing | 14.6 KB (115 KB) | 8 / 36 ms | 32 / 71 ms |
| Inventory | 40.3 KB (446 KB) | 7 / 28 ms | 105 / 155 ms |
| A vehicle | 29.6 KB (216 KB) | 7 / 30 ms | 43 / 81 ms |
| A document | 52.7 KB (233 KB) | 9 / 61 ms | 52 / 108 ms |
| The Author page | 26.3 KB (196 KB) | 8 / 40 ms | 47 / 88 ms |

The first byte is the top of the page, sent before any API read has answered, which is decision 6 at work. The drawn `#root` time includes the API's own reads inside Docker.

**Memory.** 32.9 MiB at start, 51.6 MiB idle after every view had been drawn, 126.5 MiB at the median and 135.5 MiB at the peak under ten inventory pages a second, and 120.2 MiB thirty seconds after.

**The limit, said plainly.** Ten inventory pages a second is more than one core draws. All 600 requests answered, but the processor read 96 to 104 per cent throughout, and requests queued behind each other: the whole page took 20.7 s at the median and 30.5 s at the 95th percentile. One inventory page is about 100 ms of drawing, so one core draws about ten a second and no more. The site's traffic is a small fraction of that. The step that would raise it is the edge keeping an anonymous visitor's page for a few seconds, the next item in the order this record set.

**The memory gate.** The plan's memory over the 24 hours to 10:54 CDT read 91 per cent at the median, which is 1,631 MB of B1's 1,792 (`mentor\logs\ssrlane-planread24`), and the gate was the plan's median plus the service's measured peak at or under 90 per cent. On B1 that is 1,767 MB, or 98.6 per cent, so it did not fit, and the plan was over the line before any third container existed. Steve had approved B2 beforehand, at $24.82 a month against $12.41 (ADR: One plan, two sites), and gave his word again in the session at 11:36. The plan moved to B2 at 11:42:28, which made each site unreachable for 20 to 29 seconds (`greenlane-probe-b2move.log`). On B2's 3,584 MB the same sum is 49 per cent. The service's web app, APP-THEYARD-RENDER-SS, was created on the plan at 13:18 with the measured image, and answered its readiness from both APIs.

## Where it sits

Outside the rings. The service is a client of the API, like a browser, and the API does not know it exists: no project in `api/` references it, and the host still serves the built page as a file (`ServerRenderingReadinessTests`). The service's own code is the frontend's code drawn somewhere else, with the few files of its own in `render/`.

## Files

- [`render/render.ts`](https://github.com/SteveStout/TheYard/blob/main/render/render.ts): the one function, and the stream.
- [`render/loaders.ts`](https://github.com/SteveStout/TheYard/blob/main/render/loaders.ts): what each view reads before it is drawn.
- [`render/page.ts`](https://github.com/SteveStout/TheYard/blob/main/render/page.ts): the API's page, cut at `#root`.
- [`render/server.mjs`](https://github.com/SteveStout/TheYard/blob/main/render/server.mjs): the App Service door.
- [`src/app/entry-server.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/app/entry-server.tsx): the site drawn for the service.
- [`src/app/mount.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/app/mount.tsx): where the browser takes a drawn page over.
- [`src/app/drawServer.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/app/drawServer.test.ts) and [`render/render.test.ts`](https://github.com/SteveStout/TheYard/blob/main/render/render.test.ts): every view drawn with no browser, and the service's own rules.
- [`tests/e2e/drawn.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/drawn.spec.ts): every drawn page taken over in Chrome with no hydration error.
