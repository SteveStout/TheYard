# ADR: The listing that went stale while you looked at it

Status: accepted, 2026-09-04. Found by taking a screenshot of the front page at
three widths and looking at it, which nothing else in this project does.

## What it looked like

Two screenshots of the live site, seconds apart. The first, at 1440 pixels:
three cards across the top of the inventory, chips reading `Live 7s left`,
`Live 7s left`, `Live 13s left`. The second, at 390 pixels, taken fifteen
seconds later: the first card is the same vehicle and the chip is grey, reading
`Ended Sep 4, 6:36 p.m.`

Nothing had gone wrong. Every piece of that was working exactly as designed:

- The default sort is ending soonest, which is what an auction marketplace shows
  first, and over a hundred thousand lots the soonest is always seconds away.
- The server ranks live auctions ahead of upcoming ones and both ahead of ended
  ones, which is right.
- The browser recomputes each card's status from the window as the clock ticks,
  so a countdown reaching zero flips the chip without waiting for a refetch,
  which is also right and is there on purpose.

Put together, they mean the front page is correct at the instant it is answered
and decays from the top down for as long as anybody looks at it. A minute in,
the first row is dead lots, and the visitor's first impression of a live auction
site is a wall of grey chips that will not move.

For a demo that is the whole first impression. For a portfolio it is worse than
a bug, because a reviewer cannot tell the difference between this and a site
whose clock is broken.

## Why it was not caught

Nothing in the suite watches a page for a minute. The browser tests assert what
is true when they look, which is the same instant the page was answered, and
they are right to: a test that waits sixty seconds to see if something rots is a
test nobody runs twice.

The check that would have caught it is the one that had never been run: open the
front page, wait, and look at it. That is what produced this record.

## What was already there

One timer, added when status filtering shipped:

> While a status filter is active, membership drifts as auctions open and close,
> so re-ask the server periodically and the list stays honest.

Correct, and about a different question. It answers "the set of matching
vehicles changes", so it only runs when a status filter is on. The default view
has no filter, and its problem is not membership. It is that the ranking and the
chips both go stale, which happens whether anything is filtered or not.

Another comment that was true about the question it was asked
(ADR: Broken windows, and the rule that answers them).

## Decision

Re-ask the server at the next moment its answer can have changed.

That moment is computable from what is already on the wire. Every vehicle
carries its window, so the soonest start or end still in the future, across the
vehicles on the page, is the next instant any card changes state. `nextAuctionBoundary`
returns it, or null when nothing on the page has a boundary left to cross.

Three things about the shape:

**A floor of fifteen seconds.** On the front page something ends every few
seconds, so the boundary alone would mean a request every few seconds. Floored,
an open tab asks about four times a minute and a card is stale for at most
fifteen seconds. A page whose soonest auction ends tomorrow asks nothing at all,
which a fixed interval could not manage.

**Nothing while the tab is hidden.** Nobody is misled by a stale card they
cannot see, and a background tab refetching for an hour is somebody else's
battery. The skipped refresh happens when the tab comes back.

**The server still decides.** The browser could hide an ended card itself, and
that would be cheaper and wrong: the ranking, the page count and what belongs on
page one are the server's answers, and a browser that starts editing them ends
up with a list that disagrees with `Showing 100 of 100,000`.

## Alternatives

**A fixed interval for every list.** Simple, and it asks constantly on pages
where nothing is happening while still being too slow on the page where
everything is.

**Change the default sort.** It would hide this by showing lots that end
tomorrow. The sort is not wrong; a real auction site opens on what is closing.

**Let the cards drop out client-side as they end.** Cheapest, and it desyncs the
count, the paging and the ranking from what the server believes.

**Push, over a socket or SSE.** The correct answer for a real marketplace and
the wrong one here: a persistent connection per visitor on a single container
that costs nothing per month, to save three requests a minute.

## Consequences

- The front page stays live-looking for as long as it is open, and a card is
  never stale for more than fifteen seconds.
- An idle listing, filtered to lots ending next week, makes no requests at all.
  The old timer made one a minute whenever a status filter was on.
- One mechanism now covers what two used to: the status-filter timer remains
  only for the case it is still the answer to, a filtered list with no boundary
  of its own left to cross.

## Files

- [`src/lib/auction.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/auction.ts): `nextAuctionBoundary`, and why the boundary is the moment worth asking at.
- [`src/lib/auction.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/auction.test.ts): what it returns, including the two cases that make it stop asking.
- [`src/App.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/App.tsx): the timer, the floor, and the hidden tab.
- [`api/TheYard.Domain/VehicleOrdering.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/VehicleOrdering.cs): the ranking that was never wrong.
- [`docs/ADR-055-broken-windows.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-055-broken-windows.md): the shape this belongs to.
