# React server rendering, explained

TheYard is a used-vehicle auction platform built in the open: a .NET 10 API, a React front end, and two databases underneath it. Since 1.0.3.104 its pages are drawn on a server for every visitor, by a small service that stands beside the API and asks it for data like any other client. The record of that decision, with every placement and what was rejected, is [ADR: A rendering service beside the API](https://theyard.stevenstout.biz/?doc=adr-a-rendering-service-beside-the-api). This page is the teaching half: what server rendering is, what each piece of this one does, and the number that decided each piece. Each section has an address of its own, so a link can land on it.

## In plain words

A React site can be drawn in two places. In the browser, the page arrives empty and the visitor waits while a script downloads, runs, asks the API for data and only then draws. On a server, the page arrives already drawn with its data inside it, the browser shows it at once, and React then attaches itself to what is already there instead of drawing it again. This site now does the second, for every page a visitor can link to, while the API stays a service that answers in JSON and draws nothing.

What that is worth: a visitor sees a vehicle, the inventory or a document as soon as the HTML lands, a developer can see each decision beside the measurement that made it, and the organization can explain in an interview how server rendering works from a system it built and measured.

### The words this page uses

- **Client rendering**: the browser builds the page from JavaScript after it arrives.
- **Server rendering**: a server builds the page's HTML for each request and sends it finished.
- **Hydration**: React in the browser walking HTML a server drew, checking it matches what it would draw, and attaching the clicks to it. React's own word for this is `hydrateRoot`.
- **Hydration mismatch**: the browser's first draw differs from the server's. React throws the server's markup away and draws again, which costs the visitor everything server rendering bought.
- **First load**: the data the server read before it drew the page, written into the page so the browser starts from the same data.
- **Streaming**: sending a page in parts as they are ready instead of all at once.
- **Time to first byte**: how long after asking the browser gets the first byte of the page.
- **First contentful paint, largest contentful paint**: when the first and the largest piece of the page appear.

## What a page drawn in the browser costs

Read from Steve's machine in Missouri on 8 October, on 1.0.3.99, the last build drawn in the browser: Lighthouse 12.8.2, three runs a view, the median (`ssrlane-lh-before.log`).

| Phone, Azure SQL site | First paint | Largest paint | Speed index | Blocking |
| --- | --- | --- | --- | --- |
| Landing | 1.73 s | 1.91 s | 3.39 s | 479 ms |
| Inventory | 1.53 s | 2.17 s | 5.77 s | 1,051 ms |
| A vehicle | 1.66 s | 2.47 s | 5.59 s | 98 ms |
| A document | 1.72 s | 2.47 s | 4.83 s | 396 ms |

On every view but the landing page the HTML carries no view at all: the list, the vehicle or the document appears only after the script has arrived and run and the API has answered. On a phone that shows in the speed index, 4.8 to 5.8 s on those three views against 3.4 s on the landing page, and the inventory blocks the main thread longest, at 1,051 ms. On a desk, where the network and the processor are fast, every view's largest paint reads 0.45 to 0.58 s on both sites.

## What drawing the landing page at build time bought, and what it could not

On 7 October the build started drawing the landing page into `index.html` (ADR: The landing page rendered at build time, server rendering as the goal). A browser could paint it before any script ran, and React took it over with `hydrateRoot`. Measured on a phone, the largest paint came 0.2 to 0.7 s sooner and the speed index fell by 0.4 to 0.7 s.

It could only ever do that for one page. A build runs once, so it can draw only what is the same for every visitor at every moment: the landing page with nothing signed in and nothing fetched. The inventory changes as auctions end, a vehicle's price moves with every bid, and each document is a page of its own. Those still arrived empty.

## What a rendering service is

A program that draws the page for each request. This one is about 300 lines of TypeScript in `render/`, plus a door for the host it runs on. For every address a visitor can link to, it reads from the API what that address needs, draws the site with React's server renderer, and sends the page.

```live path=render/render.ts region=the-renderer
```

It is one function, `render(url, headers, apiOrigin)`, that returns a standard `Response`. It uses only what the web platform gives every runtime: `fetch`, web streams and `TextEncoder`. That is why the same function can run on App Service under Node today and in a Cloudflare Worker later, where nothing else changes.

## Reading the data before drawing

React's server renderer does not run effects, so a component that fetches in a `useEffect` draws its loading state on a server and nothing else. The service therefore reads first and draws second. One loader per view reads what that view needs, all at once, under one deadline:

```live path=render/loaders.ts region=loaders
```

A read that fails or misses the deadline is left out, and that view draws its loading state, which the browser then fills exactly as it did before. The page is never held up for a slow read longer than the deadline, and never broken by a failed one.

## Hydration, and why it has to match

The browser's first draw has to produce exactly the markup the server drew. Anything that differs between a server and a browser has to be answered, or React discards the page. This site had four:

- **The clock.** Every countdown reads the current time. The server writes the time it drew at into the first load, and the browser's first draw counts down from that same time, then ticks.
- **The time zone.** A server does not know the visitor's. Auction stamps say UTC until React has taken the page over, then the visitor's own zone. The stamp names its zone both times, so neither is wrong.
- **The window's width.** A server has no window. `useMediaQuery` answers "does not match" on the server and during the takeover, then the window's real answer, through React's `useSyncExternalStore`, which reads a separate server value while it hydrates.
- **The document window.** A native `<dialog>` cannot be opened as a modal on a server, so it is drawn open, and opened again as a modal the moment the page is live.

```live path=src/hooks/useFirstLoad.ts region=first-load
```

Two views are left to the browser on purpose: the Admin tab and the account page. They are made of live readings and personal data that change by the second, so the server draws the frame around them and a short placeholder, and the browser draws them.

## The whole trip, drawn

[![A page drawn on the server: the first load from the address through the edge, the rendering service and the API to the HTML the browser paints, and then the takeover, where React attaches to that HTML](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/server-page.png)](https://theyard.stevenstout.biz/api/docs/diagrams/server-page)

*A preview. [Open the drawing in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/server-page) to zoom in and follow it. Every box is a file; the left lane is the first load and the right lane the takeover.*

## Streaming: the top of the page leaves first

Everything above `#root` in the page is the same for every visitor: the stylesheet, the font, the script. The service sends that part the moment a request arrives, before any API read has answered, so the browser starts downloading the stylesheet and the script while the service is still reading. The drawing follows, then the first load, then the rest of the page.

```live path=render/render.ts region=the-stream
```

React 19 can also stream inside the page, sending a slow part later behind a `Suspense` boundary. This site does not use that for data, because the data a part needs to be taken over has to reach the browser too, and React has no stable way to send it without a framework. The deadline gives the same result here: the frame first, the slow part after.

## Every decision, with the number that decided it

| Decision | The number that decided it |
| --- | --- |
| A service of its own, beside the API, not inside it | The API host carries no page framework and starts no JavaScript runtime, held by `ServerRenderingReadinessTests` since ADR-092 |
| B2 before a third container | The plan read 91 per cent of B1's memory at the median over 24 hours, 1,631 of 1,792 MB, before the service existed |
| One service for both sites | 52 MiB idle and 136 MiB at the peak, measured alone; two would have been twice that for no gain |
| Node's `http`, no framework | The image is 62.4 MB, with no `node_modules` and nothing to keep current but Node itself |
| The top of the page first | 7 to 9 ms to the first byte at the median, with the API reads still out |
| A 2.5-second deadline on the reads | The inventory is drawn in 105 ms at the median when the API answers at once; the deadline only bites when it does not |
| The page read from the API at run time | A page built into the image would name a bundle the API stops serving at its next roll |
| Caching an anonymous page at the edge, next | One core draws about ten inventory pages a second, and past that requests queue |

## Where it sits

Outside the rings of the onion. The service is a client of the API, exactly like a browser, and the API does not know it exists: no project in `api/` references it, and a test fails the build if the API host ever starts a JavaScript runtime or draws a page. Two tests hold the service's own rules: it reaches the API only, and it forwards the visitor's cookie and nothing else.

## What a Cloudflare Worker changes, and what it does not

The site's domain moves to Cloudflare at the end of October. The rendering function moves with it unchanged, behind a different door: a Worker's `fetch` handler calls `render(request.url, request.headers, apiOrigin)` and returns what it returns. What changes is where it runs, at the edge near the visitor instead of in one Azure region, and what a failure looks like: a Worker can catch a failed draw and fetch the API's page in its place, which the edge used today cannot. What does not change is everything on this page: the loaders, the first load, the four answers to hydration, and the API, which still draws nothing.

## Before and after

Read the same way on 1.0.3.104, the first build drawn on the server, from the same machine, three runs a view (`ssrlane-lh-after.log`). Before is 1.0.3.99, drawn in the browser.

| Phone, median of three | Largest paint, before | Largest paint, after | Speed index, before | Speed index, after |
| --- | --- | --- | --- | --- |
| Inventory, Azure SQL site | 2.17 s | 1.87 s | 5.77 s | 3.51 s |
| Inventory, Azure Cosmos DB site | 2.06 s | 2.05 s | 5.60 s | 3.67 s |
| A vehicle, Azure SQL site | 2.47 s | 2.16 s | 5.59 s | 2.95 s |
| A vehicle, Azure Cosmos DB site | 2.18 s | 2.20 s | 2.79 s | 4.42 s |
| Landing, Azure SQL site | 1.91 s | 2.17 s | 3.39 s | 5.69 s |
| Landing, Azure Cosmos DB site | 1.74 s | 2.18 s | 4.16 s | 5.85 s |
| A document, Azure SQL site | 2.47 s | 2.99 s | 4.83 s | 3.97 s |
| A document, Azure Cosmos DB site | 2.41 s | 2.94 s | 3.63 s | 4.48 s |

**What it bought.** The inventory, the view whose HTML used to carry nothing, reached its finished look about two seconds sooner on a phone on both sites, and a vehicle on the Azure SQL site did too. On a desk every view's largest paint stayed at 0.46 to 0.60 s on both sites, except the document. The site also reads differently to a crawler or an AI tool that runs no script: every address now arrives with its view in the HTML.

**What it did not buy.** The landing page was already drawn into the HTML at build time, so the server only made its HTML later to arrive: its largest paint moved from about 1.8 s to about 2.2 s, and its speed index swings by more than two seconds between runs, as it did before (ADR: The landing page rendered at build time, server rendering as the goal). Three runs a view are too few to call the vehicle on the Azure Cosmos DB site a loss or a gain.

**What got worse, and why.** A document read a layout shift of 1.0 on a desk, and its largest paint a half second later on a phone. The window was drawn open in the page's flow, and moved into the modal layer when React took the page over. 1.0.3.105 draws it where the modal stands, and a browser test now holds the window still across the takeover.

**Where the time goes now.** The HTML itself arrives later and carries more. Ten reads from Missouri took 196 to 241 ms to the first byte before, and 281 to 501 ms after, because the page is now drawn for each visitor and not read from a file (`ssrlane-lh-before.log`, `ssrlane-lh-after.log`). And the streaming in decision 6 is real at the service and lost at the edge: straight from the service's own address the top of the page arrived 26 to 97 ms before the drawing, and through the domain the two arrived together, 0 ms apart (`ssrlane-origin-ttfb.log`). The edge in use today holds a proxied page until it has the drawing. A Cloudflare Worker passes a stream through as it comes, which is one more reason for the move at the end of October.

## If you build one: the short list

1. Keep the API ignorant of pages. The service is a client like a browser, and a test says so.
2. Read the data before drawing, all at once, under a deadline, and let a missed read fall back to the loading state the browser already has.
3. Write what you read into the page, escaped, and start every hook from it in the browser.
4. Find every difference between a server and a browser before a visitor does: the clock, the time zone, the window, anything opened by a script. Draw each the same on both sides and change it after the takeover.
5. Draw only for the build you were made from, and send the plain page while the API rolls.
6. Draw your pages in tests with no window, one test per view, and take them over in a real browser in another.
7. Measure the service alone before it shares a machine, and decide where it runs from the memory reading, not from a guess.
8. Read the result through the edge visitors actually use, because the edge can undo what the service does.

## References

- React, [`hydrateRoot`](https://react.dev/reference/react-dom/client/hydrateRoot): taking over HTML a server drew.
- React, [`renderToReadableStream`](https://react.dev/reference/react-dom/server/renderToReadableStream): the server renderer for web streams.
- React, [`useSyncExternalStore`](https://react.dev/reference/react/useSyncExternalStore): the server value read while hydrating.
- web.dev, [Time to First Byte](https://web.dev/articles/ttfb) and [Largest Contentful Paint](https://web.dev/articles/lcp).
