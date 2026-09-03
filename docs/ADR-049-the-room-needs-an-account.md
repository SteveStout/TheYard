# ADR: The room needs an account too

Status: accepted, 2026-09-03. `POST /api/market/tick` was anonymous, and it
drove shared state from everybody's bids. A stranger with `curl` could outbid
every signed-in visitor on the site.

## Context

The simulated room (ADR: Competing bidders) is driven by the page rather than by
a timer on the server. That decision has two good reasons behind it, both still
true: the browser's midnight is what decides which auctions are live, and a
server-side timer on a free tier is a background job this project does not want
to pay for.

So the page posts a tick every eight seconds and the room bids. The endpoint
took no user, and the state it advanced came from every account:

```csharp
// Everybody's high-water marks, not one account's. The room answers a
// price rather than a person, and a room that only responded to whoever
// happened to be looking would stop being a room the moment there were two
// of them.
var buyerBids = bids.StandingAsBids();
```

That comment is right about what the room should bid against, and it is not an
argument about who may advance it. Nobody wrote the second half down, because
when it was written there was one visitor and no accounts.

## What it allowed

```
curl -X POST https://theyard.stevenstout.biz/api/market/tick \
  -H 'Content-Type: application/json' -d '{"anchor_ms": 0}'
```

No account. No cookie. Each call raises up to three vehicles, and the contested
ones go first, which means the vehicles a real person is currently winning. In a
loop, every auction any signed-in visitor holds gets counter-bid up to the
ceiling, and the raised prices show on the public listing for everyone until the
container rolls. Nothing attributes it to anybody: the request ring records a
method and a path.

The damage is invented bids on a demo. The shape is an unauthenticated write to
shared state, and it is the same shape as the reset that deleted everybody's
bids, one step worse, because that one at least needed an account.

## Decision

The tick requires an account. What it bids against does not change.

```csharp
}).RequireAuthorization();
```

The room still answers the standing price rather than a person, which is the
part the original comment got right and which keeps the room a room when two
people are in it. What changed is that moving it costs the same as bidding,
which is a sign-in, and every advance now belongs to somebody.

The page stops asking when signed out:

```ts
if (!accountKey) return;
```

Without that, a signed-out tab would poll for a 401 every eight seconds forever.
A visitor who has not signed in has nothing bidding against them, which is the
honest state rather than a missing feature.

## What this costs

The grid no longer moves for a signed-out visitor. That was a real thing the
old behaviour bought: a page with prices ticking up looks alive, and a
first-time visitor now sees a still one until they make an account.

It is the right trade anyway. The alternative is an endpoint anybody can use to
change what everybody else sees, defended by the observation that the data is
not worth anything, and that defence stops working the first time the data is.

## Rejected

**Rate limiting it and leaving it open.** It reduces the rate of the problem and
keeps the property that an anonymous stranger can move other people's auctions.
Rate limits are worth adding here for other reasons, and they are not a
substitute for knowing who is asking.

**Scoping the tick to the caller's own bids.** This sounds tighter and makes the
room worse: two people bidding on the same car would each see a different
competitor, and the room would stop being a shared room, which the original
comment predicted correctly.

## Consequences

- Every advance of shared auction state belongs to an account.
- Signed-out visitors see a still grid. Signing in starts the room.
- One fewer anonymous write endpoint. What remains is `POST /api/errors/client`,
  which is deliberate and bounded, and which should get a rate limit.

## Files

- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the tick, and the region banner that claimed a single anonymous buyer long after there was not one.
- [`src/hooks/useBids.ts`](https://github.com/SteveStout/TheYard/blob/main/src/hooks/useBids.ts): the loop that no longer runs signed out.
- [`docs/ADR-027-competing-bidders.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-027-competing-bidders.md): why the page drives the round at all.
- [`docs/ADR-048-reset-is-one-persons.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-048-reset-is-one-persons.md): the same shape, found an hour earlier.
