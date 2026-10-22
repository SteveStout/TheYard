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

## Addendum, 2026-10-21 (1.0.4.19): what the store covers, what sits outside it, and the quiet refresh

Three sentences above stopped being true on 21 October, read by the review of 1.0.4.11 to 1.0.4.18. The status line's "every request the site makes passes through one small store" was not so: `src/lib/stores.ts` and seventeen component fetches never reached it. "No call site in a component changed" was not so either: 1.0.4.17 itself changed `useRunningBuild.ts`, `useAdminReads.ts` and `useCloudGlances.ts`. And "a refresh tick ... never counted" held for the tick and not for what it set off: the listing's timer, the room's round and a room price move on the open vehicle all refetched through the counter, so an untouched Cosmos DB page refreshed itself through it at 172 s.

From 1.0.4.19 the store covers three seams and one rule. The request files (`data.ts`, `auth.ts`, `prefetch.ts`, and now `stores.ts`, the Store bar's read on load, marked background). The person-started reads in the Admin cards and the document window, now through `counted`: a card opening (`useRead`), a kept list's window (`useKeptWindow`), the machines' window (`useMachines`, whose 30 second timer is background), the kept log, the activity report and its visitors, the tests card, the three Admin POSTs (a reset link, a page sweep, a proof run) and a sidebar document (`DocDialog.tsx`). And the quiet refresh: `FetchVehiclesOptions` and `fetchVehicleById` take `background`, `useInventory`'s `refreshList(quiet)` passes it on a refresh, the listing's timer and the room's round ask quietly, the open vehicle's price move reads in the background, and a Retry stays loud, so a page nobody touched never locks and a click on Retry does.

What stays outside the store is named, line by line with its reason, in `WorkingLockTests.Every_fetch_in_src_goes_through_the_store_or_is_a_named_background_read`, which scans every file under `src` (not the tests) for a raw `fetch(`: the tagged-open report, the Admin strip's activity read, the pages card's own read, a running proof's poll, the landing page's two reads, the pulse band, the error report and the recommendations column. Every one is a read nobody clicked for. `src/lib/data.test.ts` holds the quiet refresh (a background `forceRefresh` and a background vehicle read never lock; the same refresh a person asked for does), and `tests/e2e/working.spec.ts` a page left alone across a routed 1,500 ms refresh that never sets `data-locked`, and a Retry that does.

## Addendum, 2026-10-21 (1.0.4.20): a hold, a way out, and a status line that speaks once

Measured live on 1.0.4.18, a lock of 2 ms on a 509 ms answer was followed by the 180 ms fade, so an answer between 500 and about 560 ms showed a ghost of the panel: the fade was the flash this record says never happens. A hung bid or sign-in held the page frozen with no way out, since every key is swallowed and no request carried a time limit. And the lock was its own live region: it appeared whole with static text, and the dots changed generated content inside it every 300 ms.

From 1.0.4.20, three changes. **A hold:** once the lock has shown it stays at least `HOLD_MS` (350 ms) in `src/lib/working.ts`, so it is either absent or plainly there; a request that would clear it at 510 ms clears it at 850. `FADE_MS` reaches the CSS as the `--working-fade` custom property the component sets, so the figure lives in one place. **A way out:** `giveUp()` in the same file is a signal that aborts after `GIVE_UP_MS` (twenty seconds) on the page's own timer; `postJson` in `data.ts` (every bid, buy and the room's round) and `submit` in `auth.ts` (sign in, register) hand it to their fetch, so a hung request rejects, the caller's own error path shows its sentence, and the lock lets go. Escape stays swallowed: the lock is the design, and the twenty seconds bound it. **A status line:** a visually hidden `role="status"` line is always in the page and its text flips to "Working" while the lock is up and back to nothing after, so a screen reader hears it once; the lock itself is a modal named Working with no live region, and the turning dots sit in an `aria-hidden` span. When the lock lets go, focus goes back to the element that had it if it is still in the page, and to the main landmark (which takes focus, `tabIndex` -1) when the answer replaced it.

`src/lib/working.test.ts` holds the hold and the give-up; `src/components/shared/WorkingLock/WorkingLock.test.ts`, the suite's first component test (a `.ts` file drawn with `renderToStaticMarkup`, because the unit suite runs `*.test.ts` in Node with no DOM), holds the status line, the modal's name, the hidden dots, the fade handed to the CSS, focus going back or to main, and the swallowed click; `tests/e2e/working.spec.ts` a sign-in routed to hang 25 s that lets go at about twenty seconds with the error shown.

## Addendum, 2026-10-21 (1.0.4.24): every request is written in src/lib

The list of fetches outside the store that 1.0.4.19 named is gone. Every component that read the network now asks `request(url, init, options)` in `src/lib/request.ts`, which is `fetch` inside `counted`, so a raw `fetch(` anywhere outside `src/lib` is refused by `WorkingLockTests` and by `FileHeaderTests`, and the reads nobody clicked for (the landing page's two, the pulse band, the recommendations column, the error report, the tagged-open report, the Admin strip's activity, the pages card's own read, a running proof's poll) say `background` where they are asked.


## Addendum, 2026-10-22 (1.0.4.28): the cover reads over white

Measured with the lock forced on, the cover was a 35 per cent white tint with a 14 px blur: frost over the content, and nothing visible over the sidebar and the store bar, which are white, so the lock looked as if it stopped at the content's edge although it covered and took clicks across the whole page. From 1.0.4.28 the cover mixes 6 per cent of the header's teal into a 38 per cent white tint, and keeps the blur, so it reads over white surfaces too.
