# ADR: One working lock for the whole site, after half a second

Status: accepted, 2026-10-21. When a request somebody's click started has run half a second, one lock covers the whole page until the last such request settles. It is registered once, in the shell, and every request the site makes passes through one small store that decides when it shows. It names no action.

## In plain words

A click that answers fast should look instant. A click that does not should say the site heard it, and stop a second click from landing on a page that is about to change. The lock is that: frosted glass over the page, the page still showing through, a ring that turns and the word "Working". It comes only after half a second, so a quick answer never flashes it, and it goes the moment the answer arrives.

What that is worth: one rule for every slow moment on the site, written once, instead of a spinner in each place that happens to be slow today.

## Context

Steve asked on 21 October, 09:00 to 09:06 CDT, for "a loader screen when you click a button and the website is working ... if a query randomly takes longer than .5 sec", that "locks the screen", looks "modern and transparent", and is "a global one we can register in the react" with no action named. He approved look D of mentor\theyard-loader-mock-2026-10-21.html: the HUD ring inside a glass panel.

Measured in Chrome before the change, on the live site at 1.0.4.10, 21 October about 11:20 CDT, reading the browser's resource timings for the API request behind each click (images left out):

| Click | theyard.stevenstout.biz (SQL) | theyard-cosmos.stevenstout.biz (Cosmos DB) |
| --- | --- | --- |
| Change the make filter (seven makes each) | 156, 170, 173, 240, 287, 333, 542 ms | 435, 457, 510, 525, 629 ms (two more answered from the page's cache) |
| Load more | not measured | 215 ms |
| Open a vehicle from the list | no request: the list already holds it | not measured |
| Back | no request: the page's cache answers | not measured |
| The page's own first reads (stores, me, bids, version) | 141 to 162 ms | 112 to 119 ms |

Place bid, Buy now and Sign in were not measured: each needs a signed-in session or a password typed on the live site, and this session does neither there.

## Decision

**Half a second, not 300 ms.** The session file carried 300 ms; Steve's own words were half a second, and the table settles it: at 300 ms, every Cosmos DB filter change measured would have locked the page for 135 to 329 ms, a flash, which is what the lock must never do. At half a second the Cosmos DB filter changes sit on either side of the line and the SQL ones lock about one time in seven. `LOCK_AFTER_MS` in `src/lib/working.ts` is the one place the figure lives.

**One store, no React.** `src/lib/working.ts` holds `begin` (a token), `end` and `subscribe`. A request that has run past half a second is counted; the lock is up while any counted request is in flight and clears on the last one, answered or failed. `counted(run, options)` wraps a fetch in begin and end, and `src/lib/data.ts`, `src/lib/auth.ts` and `src/lib/prefetch.ts` call it around their fetches, so no call site in a component changed. `src/hooks/useWorking.ts` reads it with `useSyncExternalStore`, and a page drawn on a server is never locked, so hydration matches.

**Background reads never lock.** A read nobody clicked for says so with `{ background: true }` and is never counted: the Admin tab's thirty-second strip, the rail's glances, the footer's version poll, the room's round of bidding, the buyer's bid map, the filter dropdowns' values and every prefetch. A person's action counts: a filter change, load more, opening a vehicle by its address, a bid, a buy, a reset, signing in or registering.

**One component, mounted once.** `src/components/shared/WorkingLock` is drawn once in `src/app/TheYard.tsx`, beside the app inside the error boundary, and nowhere else. While it is up it covers the page, takes every click and key, keeps focus on itself and gives it back after, and has the role status with the name Working. It fades in and out over 180 ms; while nothing is slow it stays in the page with display none (so no faded word is ever left in the page), so no state in the component follows the store (the lint gate refuses a state set inside an effect). Its colours are the palette's tokens; the reduced-motion setting slows the sweep. The Style section's colour page draws its panel, still, from the same component (the `working-lock` fence).

**No caption.** The words are fixed, "Working" over "the yard is answering". A per-click caption would need every caller to name itself, and the point is one lock nobody has to remember.

## Consequences

- A request the store does not see (a component's own fetch, the Admin cards' reads when a card is opened) never locks the page. The cards are read by the Admin tab's own loop and are background by nature; a new user action that fetches outside the three files has to go through `counted` to be seen.
- A slow Cosmos DB filter change, over half a second, locks the page until it answers; one just under does not. That line is the owner's figure, and the table above is why it is not lower.
- The lock blocks input on the whole page, the rail and the header included, while it is up.

## Where it sits

Only the front end. `src/lib` gains the store, which imports nothing from React, so a test drives its half second by hand; `src/hooks` gains the one hook that reads it; `src/components/shared` gains the lock and its still look, mounted once by the shell in `src/app`. The three files that fetch (`data.ts`, `auth.ts`, `prefetch.ts`) wrap each request in `counted`, so no call site changes. The server and the onion are untouched. Single responsibility: the store decides when, the lock only draws. What would change it: a request that must never block the page, which says `background` where it begins.

## What holds it

`src/lib/working.test.ts`: a request that ends before half a second never locks; one that ends at 900 ms locks from 500 to 900; two overlapping unlock on the last; a background request never locks; a failure unlocks and still reaches the caller. `tests/e2e/working.spec.ts`: one filter request routed through a 1,500 ms delay shows the lock (role status, name Working) within 1,200 ms of leaving, before the delayed answer can arrive, a click on a vehicle during it opens nothing, and the lock is gone after the answer; a filter answered at once never shows it. `src/library/styleBlocks.test.ts` holds the Style page's box. `WorkingLockTests` holds the shape: the shell mounts the lock and no other file does, the wait is 500 in the one store, and every fetch in `data.ts` and `auth.ts` sits inside `counted`.

## Files

- [`src/lib/working.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/working.ts) - the store and `counted`
- [`src/hooks/useWorking.ts`](https://github.com/SteveStout/TheYard/blob/main/src/hooks/useWorking.ts) - the hook
- [`src/components/shared/WorkingLock/`](https://github.com/SteveStout/TheYard/tree/main/src/components/shared/WorkingLock) - the lock and its look
- [`src/app/TheYard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/app/TheYard.tsx) - where it is mounted
- [`tests/e2e/working.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/working.spec.ts) - the browser proof
