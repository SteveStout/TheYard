# ADR: Competing bidders

Status: accepted, 2026-09-03, shipped as 1.0.0.37. The README listed this as
open work: "simulated competing bidders so the high-bidder state can be lost,
with outbid alerts."

## Context

The demo had one buyer, and that buyer could not lose. Place a bid and you hold
it forever: the High bidder chip never comes off, the reserve badge never
changes hands, and "you have been outbid" is a state the code can describe but
never reach. Half the bidding rules were therefore unexercised by anything a
visitor could do. `BidRules.MinNextBid` climbing a tier, a reserve going from
unmet to met by somebody else's money, the sinking feeling of refreshing to a
higher number: none of it happened.

The README also listed Server-Sent Events, to push those changes rather than
poll for them. That is not what shipped, and the reason is written down below
rather than left as a gap.

## Decision

**A simulated room, held the same way the buyer's bids are.** `MarketService`
keeps a dictionary of vehicle id to competing bid, exactly as `BidService`
keeps the buyer's, and layers it over the shared dataset as an overlay rather
than a mutation. Two overlays, composed at the composition root, in an order
that matters: the buyer's first and the room's second, and the room only wins
where it is actually higher. Reverse them and the buyer always appears to be
winning, which is the bug this whole feature exists to make impossible.

**The room bids through `BidRules`, like everything else.** It calls
`MinNextBid` for its amount and `ResolveBid` to check itself. A simulated
bidder allowed to place bids the rules forbid would be a second, quieter
implementation of the auction, and the onion architecture here exists
specifically so there is only one.

**Three limits keep it a demo rather than a slot machine.** It leaves the
buyer's lead alone for twenty seconds before answering it, because a room that
outbids you one second after every bid is not competition. It stops at twice
the opening ask, because a competitor with no ceiling wins every auction and a
demo the visitor cannot win is worse than no competitor at all. And it never
bids at or above buy-now, because that bid would win outright under the rules
and end an auction the visitor was in.

**The page drives the round, not a timer on the server.** `POST
/api/market/tick` runs one round; the browser calls it every eight seconds
while the tab is visible. Two reasons. The anchor: the browser's local midnight
decides which auctions are live (the auction schedule is derived from that anchor), and a room bidding
against a different set of live auctions than the visitor can see would be a
bug nobody could reproduce. And cost: the room moving in an empty container is
work nobody sees, on a free tier where that is the only kind of work worth
avoiding.

**Whether you are still winning is the server's answer.** `/api/bids` returns
`outbid` and `market_amount` per vehicle rather than the browser comparing two
numbers itself. Same principle as every other rule in this system, and the same
reason: two implementations of one rule eventually disagree, and the one in the
browser is the one that will be wrong.

## Why not Server-Sent Events

The README asked for SSE and this is polling instead. The honest reason is the
edge.

Phase one terminates TLS on a Netlify site that proxies every request to the
container with a rewrite rule (ADR: Front Door origin, ADR: Edge deploy economics). A rewrite
proxy buffers, and a long-lived streaming response is exactly what it buffers
worst; the connection would also have to survive a proxy timeout it was never
designed to hold open. I could not verify any of that without changing the
edge, and the edge is out of scope for this session by a rule that exists
because a change there costs real money on a free tier.

So: an eight-second poll of one small endpoint, which works through any proxy
and costs a request the size of a bid map. When the edge becomes Front Door,
which ADR: Front Door origin already plans, SSE becomes a change to one endpoint and
one hook, and the rest of this design does not move. Writing that down is worth
more than shipping a streaming endpoint that silently does not stream.

## What it looks like

The room has answered a bid on the live site. The green line said "You're the
high bidder at $48,500" a moment before this:

![The vehicle detail page: the bid stands at $49,000 and a red panel reads "Someone outbid you"](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-outbid-panel.jpg)

And the same vehicle back in the grid, with the chip that could never appear
before this change:

![The inventory grid with a red Outbid chip on the first card, which now stands at $49,000 with 26 bids](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-outbid-grid.jpg)

The first of those two screenshots found a defect on its way into this record.
It was taken the instant the outbid line appeared, and it showed "minimum
$49,000" beside a standing bid of $49,000, which is arithmetically impossible:
the browser was still holding the minimum from before the room's bid, and the
refetch had not landed. Typing the number the page displayed would have been
rejected. The minimum is domain math and the browser will not recompute it, but
it can tell that the one it holds is impossible, so for that moment the panel
now says it is updating and the button waits rather than showing a number it
cannot vouch for.

## In the code

The overlay, and why the order is the way it is
(`api/TheBlock.Application/MarketService.cs`):

```live path=api/TheBlock.Application/MarketService.cs region=apply
```

One round of bidding, and the three limits:

```live path=api/TheBlock.Application/MarketService.cs region=tick
```

Where the two overlays are composed (`api/TheBlock.Api/Program.cs`):

```live path=api/TheBlock.Api/Program.cs region=overlays
```

The endpoints (`api/TheBlock.Api/Program.cs`):

```live path=api/TheBlock.Api/Program.cs region=market-endpoints
```

The answer the badge needs, derived server-side
(`api/TheBlock.Api/BidViews.cs`):

```live path=api/TheBlock.Api/BidViews.cs region=views
```

The browser's half: the round it asks for, and the sentence it shows
(`src/hooks/useBids.ts`, `src/components/BidPanel.tsx`):

```live path=src/hooks/useBids.ts region=market-loop
```

```live path=src/components/BidPanel.tsx region=outbid
```

And the guard that keeps the panel from offering a minimum it cannot vouch for:

```live path=src/components/BidPanel.tsx region=stale-minimum
```

## What the review caught before this shipped

Two adversarial passes over this change, one on the API and one on the browser,
found eleven defects. Four mattered, and all four are worth reading because
none of them is the kind a test suite finds by accident.

**A bid could be accepted below the going rate.** `BidService.Apply` overwrote
the price rather than taking the highest, so when the bid endpoint handed it a
vehicle the room had already raised, the buyer's own older bid was written back
over the room's higher one. The minimum next bid was then computed against the
wrong number. The same response body advertised a minimum of $24,800 and
enforced $23,800. A visitor could sit permanently one increment under the room
and be accepted every time. `Apply` now takes the max, the way the room's
overlay always did, and `BidServiceTests` holds the composed price.

**Two bids at once could lose the higher one.** `PlaceBid` and `BuyNow` are
read, decide, write on a singleton; a `ConcurrentDictionary` makes each of the
three atomic and the sequence of them not. Two posts, a double click, and the
lower bid lands second. Worse across the two methods, where an ordinary bid
landing after a buy-now flipped the sold flag back to false and put the vehicle
back in the room's reach. Both are under a lock now.

**A tick landing after a bid erased the bid.** The browser replaced its whole
bid map with each tick's answer, so a round already in flight when the visitor
bid returned a snapshot from before it and wiped the confirmation off the
screen for eight seconds. The server had accepted the bid; only the page said
otherwise, which on an auction screen reads as money going nowhere. Every
action the buyer takes now bumps a counter that a stale round is checked
against.

**The room hammered the same three cars.** The grace period was checked only
for vehicles the buyer had bid on, and the candidates arrived in a stable
order, so the three soonest-ending live auctions were raised every eight
seconds and doubled in about two minutes while the other thirty-seven never
moved. Grace now applies to every vehicle, and the uncontested candidates are
shuffled.

The rest were smaller and are fixed in the same commit: two full dictionary
copies per inventory request taken only to read a count, a tick endpoint that
sorted forty thousand live auctions to keep forty of them, a bid count that
fell when the buyer retook a lead, a minimum-next-bid on the detail page that
went stale the moment the room answered, focus being stolen when a deep-linked
vehicle finished loading, and the Admin panel opening at the list's scroll
offset with its heading above the fold.

One finding was a documentation defect rather than a code one, and it is the
one most worth naming. A comment on the new outbid style said its colours were
asserted against WCAG AA in `tokens.test.ts`. They were not. Both pairs happen
to pass, so the claim was true by luck and false as a guarantee. The assertion
now exists.

## Consequences

- The high-bidder chip can now come off, and the bid panel says who is ahead.
  That was the point.
- Bidding is validated against the room's standing price, so the minimum next
  bid climbs as the room bids. A visitor who walks away and comes back finds a
  more expensive auction, which is what an auction is.
- Reset clears both sides. Clearing the buyer's bids and leaving the room's
  standing would read as a bug however carefully it were explained.
- The room only moves while a tab is open. Leave the site for an hour and
  nothing happened while you were gone. That is a demo's honest shape, and the
  alternative is a background service burning a free tier's CPU for an empty
  room.
- Eight seconds is a compromise, not a measurement. Faster feels more alive and
  costs more requests; slower is cheaper and feels dead. It is one constant in
  one file.
- Still open, and now genuinely next: per-user identity, so the room is other
  people rather than a simulation, and the streaming transport above once the
  edge can carry it.

## Files

- [`api/TheBlock.Application/MarketService.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Application/MarketService.cs): the room, its overlay and its three limits.
- [`api/TheBlock.Api/BidViews.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/BidViews.cs): whether the buyer is still ahead, decided server-side.
- [`api/TheBlock.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Program.cs): the composed overlays, the tick endpoint, and bidding against the room's price.
- [`api/TheBlock.Application/BidService.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Application/BidService.cs): the buyer's side, now stamped with when each bid was placed.
- [`src/hooks/useBids.ts`](https://github.com/SteveStout/TheYard/blob/main/src/hooks/useBids.ts) and [`src/lib/data.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/data.ts): the round the page asks for.
- [`src/components/BidPanel.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/BidPanel.tsx) and [`VehicleCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/VehicleCard.tsx): the sentence and the chip.
- [`api/TheBlock.Tests/MarketServiceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/MarketServiceTests.cs): every limit, held.
- [`tests/e2e/market.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/market.spec.ts): a bid, a round, and the badge changing hands in a browser.
