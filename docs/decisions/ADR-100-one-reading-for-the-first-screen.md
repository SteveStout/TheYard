# ADR: One reading for the first screen

Status: accepted, 2026-10-17. The landing page reads `/api/pulse`, one small public object built from the sources the Admin tab already reads, kept for fifteen seconds and read again every fifteen while the page is open and visible. Every figure in it is nullable, and a null means unread rather than zero.

## In plain words

The first screen now shows what the site is doing while you look at it: how long it takes to answer, what it has cost Azure this month, and how long it has been up since the last deploy. Those numbers come from one small request rather than from the two heaviest reads on the site, so a page anyone can open does not cost what the operator's own tab costs.

What that is worth: a stranger sees a running system rather than a description of one, and the page that proves it stays cheap enough to leave open.

## Context

The three figures already existed. How fast the site answers is folded into the kept machine minutes every minute (ADR: What the machines are doing). What Azure charges is read once an hour and kept (ADR: What Azure charges). The uptime and the build are on the health report and the version route.

Reading them the way the Admin tab reads them would have put `/api/admin/machines` and `/api/admin/costs` on the landing page. Those two are the slowest reads on the site: the machines read queries the document store for a day of buckets, and the cost read walks the kept days. The landing page is the one page a stranger opens, often on a phone, and it is also the page the rendering service draws ahead of time.

## Decision

**One route, public, and small.** `GET /api/pulse` answers ten fields and nothing else. It is public because the readings it carries are already public: the cost cards are public on the owner's own ruling, and the machines card is public beside them. It carries no visitor, no address and nothing per person.

```live path=api/TheYard.Api/Features/Health/PulseEndpoints.cs region=pulse-route
```

**Fifteen seconds, held once.** The answer is built at most once every fifteen seconds and handed to every caller in that window, under one gate, so a burst of arrivals costs one query rather than one each. Fifteen because the page reads again at the same interval: a reader watching the band sees it change within a quarter of a minute of the site changing, and a page left open all day costs four reads a minute whoever is looking at it. The page stops reading while the tab is hidden, so a phone in a pocket costs nothing.

```live path=api/TheYard.Api/Pulse.cs region=pulse-reader
```

**The usual five minutes, not the newest bucket, and not the day's worst.** The latency figure is the request-weighted median of the five-minute ninety-fifth percentiles over the last day. The newest bucket alone would read 1 ms on a quiet minute, which is true and reads as a trick. The day's worst minute is also true and reads as a fault. What a reader wants to know is how long this site usually takes at its slow end, and that is the middle five minutes by request count.

It is not the ninety-fifth percentile of the day, and the field is not named as though it were: the durations themselves are not kept, only each five minutes already folded to its own counts, where a bucket's own figure is its worst minute's ninety-fifth. A true figure for the day cannot be computed from what is kept, so the field is `usual_p95_ms_24h`, the line under the instrument says what it is, and the number of requests it rests on sits beside it. Taking each five minutes at its worst minute leaves the figure cautious rather than flattering, which is the direction a number on a public page should err in.

**A null is unread.** Every figure is nullable and the band prints the word rather than a number when it is missing: the cost reader answers nothing on a developer's machine, and the kept minutes answer nothing on a container with no document store. A zero in either place would claim the site cost nothing or answered in no time.

## What this cost

The route is forty lines of handler and a reader of about a hundred, and it duplicates no arithmetic: the buckets and the bill are the same objects the Admin tab draws. The one number it computes itself is the weighted median above, which `PulseTests` holds against hand-written buckets, including a day with nothing in it.

## Alternatives

**Read the two Admin routes from the landing page.** Rejected: the two heaviest reads on the site, on the landing page, which every visitor opens first, several times a minute.

**Push the figures with a stream.** Rejected for the reason the auction room already polls rather than streaming: the edge in front of this site buffers streamed responses (ADR: The room bids back). A fifteen-second read is simpler and survives the edge.

**Put the numbers in the sentence.** That is where they were. A sentence with numbers in it goes stale the day the numbers move, and nobody edits a sentence. A reading that is read is never stale or it says so.

## Where it sits

The API layer and the landing page, nothing between. `PulseReader` is one class in `TheYard.Api` that asks the two sources the Admin tab already asks and keeps the answer for fifteen seconds; the endpoint hands it out; `PulseBand` draws it. Nothing in Domain or Application knows the band exists, and the band knows no arithmetic: what a figure means is decided once, on the server. What would change it is a third reader of the same two sources, at which point the keeping belongs to a reader of its own rather than to this one.

## Files

- [`api/TheYard.Api/Pulse.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Pulse.cs) - the shape, the cache and the weighted median
- [`api/TheYard.Api/Features/Health/PulseEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Health/PulseEndpoints.cs) - the route
- [`api/TheYard.Tests/PulseTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/PulseTests.cs) - what the figure is, and what an empty day answers
- [`src/lib/pulse.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/pulse.ts) - the shape on the page, and the words each figure is printed in
- [`src/components/landing/PulseBand/PulseBand.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/landing/PulseBand/PulseBand.tsx) - the band
