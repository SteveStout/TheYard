# ADR: Accounts and per-user bids

Status: accepted, 2026-09-03. Steve's ask: "replace the single anonymous
in-memory buyer with authenticated users whose bids persist." The decision he
had already made, and which this follows: ASP.NET Core Identity with a JWT.

## Context

ADR: The relational store gave bids somewhere to live. It did not give them an
owner. `BidRow` was keyed on the vehicle alone, `BidService` held one
dictionary, and the comment on it said what that meant: "the single anonymous
buyer's bids".

So "you are the high bidder" was a statement about the only person in the room,
and the only thing that could take a lead away was the simulated room from
ADR: Competing bidders. Two visitors on the live site shared one set of bids and
neither could tell.

## Decision

**Identity for the accounts, and nothing else from it.** `AddIdentityCore` gives
`UserManager<YardUser>`, the password hasher, the normalised lookups and the
seven tables that go with them. What it deliberately does not add is Identity's
own cookie authentication, because the session here is a token this service
signs and reads. Writing the account tables by hand would be a week of getting
the boring half of authentication subtly wrong.

**A JWT in an httpOnly cookie.** Three choices in one sentence, so each on its
own:

*A token rather than a server session*, because the API is stateless and a
signature is cheaper to check than a lookup. Nothing here needs to revoke a
session mid-flight; if it did, this would be the wrong answer.

*A cookie rather than the `Authorization` header*, because the browser sends it
without the page having to hold it.

*httpOnly rather than `localStorage`*, which is the part that matters. A bearer
token is the user: whoever holds it is them. `localStorage` hands it to every
script that runs on the page, including anything that ever gets injected into
one. httpOnly means the page never has it and cannot leak it, and the cost is
that the client code gets simpler rather than harder.

**The signing key is configuration, and its absence is a random key.** Without
`Auth:SigningKey` the process invents thirty-two random bytes and logs that it
did. The consequence is honest and stated in the log: a deploy signs everybody
out. The alternative that a repository must never take is a committed default,
because a signing key in source control is every session forever, for anyone who
reads the repository.

**Two indexes over the same bids.** `BidService` keeps them by vehicle and by
user, because two questions are asked at very different rates. What does this
vehicle stand at is asked a hundred thousand times per listing request; what
have I bid is asked once. The first is the hot path and stays a single
dictionary lookup, exactly as it was when there was one buyer.

**One price for everybody.** `Apply` takes no user on purpose. A listing shows
one number, and a price that depended on who was looking would be a different
auction per visitor.

**Reads stay open; the three endpoints that change something refuse.** Placing a
bid, buying now, and the reset all require an account, and so does the bid
history. `GET /api/bids` answers an empty map when signed out rather than 401,
because the page asks for it on every load and "you have no bids" is the true
answer for somebody who has not signed in. An auction nobody can watch without
signing up is a worse demo and no safer.

**Wrong password and no such account get the same answer.** Two messages would
be an endpoint that tells a stranger which email addresses are registered here.

**The password rule is length and nothing else.** Eight characters, no required
symbol, no required digit, no required case. The composition rules mostly teach
people to write the password down, which NIST 800-63B says at more length than
this record has room for.

## In the code

Issuing the session (`api/TheYard.Api/Tokens.cs`):

```live path=api/TheYard.Api/Tokens.cs region=issue
```

Reading it back, and who is asking (`api/TheYard.Api/Tokens.cs`):

```live path=api/TheYard.Api/Tokens.cs region=who
```

Two indexes, one fact (`api/TheYard.Application/BidService.cs`):

```live path=api/TheYard.Application/BidService.cs region=record
```

What a badge is told (`api/TheYard.Api/BidViews.cs`):

```live path=api/TheYard.Api/BidViews.cs region=views
```

The tests, including the two accounts and the restart
(`api/TheYard.Tests/AuthTests.cs`):

```live path=api/TheYard.Tests/AuthTests.cs region=auth-tests
```

## What the suite found on the way

Giving the tests accounts turned two of them intermittently red, about one run
in three, with nothing to go on but "Expected: OK, Actual: BadRequest". The
first fix was to make the assertion carry the server's own sentence, which
every rejection on this API already has (ADR: Error handling). The reason it
reported was the auction had ended.

`AuctionClock` carries two instants, and only one of them is the anchor a test
pins. `NowMs` is real wall-clock, read per request, and liveness is judged
against it. The default sort is `EndingSoonest`, so `status=live&limit=1`
returns the auction with the least time left in the whole dataset. Under the
full suite several hosts expand a hundred thousand vehicles at once, the gap
between reading `min_next_bid` and posting the bid stretches to seconds, and
that vehicle closes inside it.

`BidFlowIntegrationTests` never had this, because it already sorted by most
bids, and so did the browser suite, whose comment says it plainly: "the default
sort's first card can expire within seconds". Three API tests had not learned
it. They have now, and the reason is written where the query is rather than in
a commit message.

Six consecutive runs is what settled it. One green run after a change that
touched the failing area proves nothing about a test that was already passing
one run in three.

## What this is not

**Not production authentication.** There is no email verification, no password
reset, no lockout after repeated failures, no refresh token, and no roles. A
real deployment would put this behind Auth0 or the organisation's SSO and keep
none of it, which is how it was run at Conway. What is here is the smallest
thing that makes the auction's central claim true: that two people can bid
against each other and the system can tell them apart.

**Not a cookie that is Secure everywhere it should be.** The origin is reached
from the edge over plain HTTP, which ADR: Edge economics records as the price of
the free tier, so `Request.IsHttps` is false on a request that was HTTPS the
whole way to the visitor. The forwarded header is what carries that fact across
the hop, and the cookie is marked Secure when it says https. The hop itself is
still plain, and a session cookie inherits that exposure along with everything
else on it; the fix is the same one that record already names.

## Consequences

- A bid belongs to somebody, survives a restart, and can be lost to another
  person rather than only to the simulated room.
- Without the store there are no accounts, so a container that falls back to the
  file readers (ADR: The relational store) is browse-only. The health check says
  which mode it is in, and the account endpoints answer 503 rather than 500,
  because nothing is broken and a dependency is missing.
- The demo's reset button clears everybody's bids, not just the caller's, since
  the simulated room's are shared and a reset that left half an auction standing
  reads as a bug however carefully it is explained.
- `BidServiceTests` changed in one mechanical way, an account argument, and in
  no other: what those tests assert about the bidding rules did not move, which
  is the evidence that the rules and the ownership are separate things.
- Two flakes that predate this work are gone with it. The API tests now bid on
  a vehicle with time left on it, and the search benchmark measures the best of
  five runs against a quarter of headroom rather than asserting `with <=
  without` under a doc comment claiming to be loose.

## Files

- [`api/TheYard.Api/Tokens.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Tokens.cs): the token, the cookie, and who is asking.
- [`api/TheYard.Api/Accounts.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Accounts.cs): what a register or login answers with.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the composition, the endpoints, and the three that refuse.
- [`api/TheYard.Application/BidService.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/BidService.cs): the two indexes.
- [`api/TheYard.Application/Ports.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Ports.cs): the store port, now keyed on the pair.
- [`api/TheYard.Infrastructure/YardUser.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/YardUser.cs): the one field Identity does not already have.
- [`api/TheYard.Tests/AuthTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/AuthTests.cs): the proof.
- [`docs/ADR-038-identity-explained.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-038-identity-explained.md): the same setup, walked at a new developer's level.
- [`docs/ADR-033-relational-store.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-033-relational-store.md): where bids got somewhere to live.
