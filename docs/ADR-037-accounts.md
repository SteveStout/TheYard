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
`Auth:SigningKey` the process invents thirty-two random bytes and, since
1.0.0.109, logs a warning that it did; until then the comment promised the log
and nothing wrote it. The consequence is honest and stated in the log: a deploy
signs everybody out, which is what the deployed containers have done on every
roll, because nothing configures the key there yet. The alternative that a repository must never take is a committed default,
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

## Addendum, 2026-09-08: the answer that arrived late

The browser suite runs three times before a version ships, and on the second
run of 1.0.0.92 the account test went red once with nothing to go on but the
rail not showing the address. The page snapshot Playwright keeps said more:
the registration had answered 200, the heading with the address had been
visible, and by the time the test looked at the rail the whole view had gone
back to "Sign in to bid" with an empty form. Something had signed the new
account out again, and nothing had asked it to.

The page asks who is signed in once, when it opens, because the session is an
httpOnly cookie the page cannot read. That question went out to a server that
was running the whole suite at once, so it answered slowly, and in the gap the
test filled the form and registered. "Nobody" was the true answer when the
question was asked and a stale one by the time it landed, and it was applied
as if it were current. A visitor on a slow connection who types fast can do
the same thing to themselves; the suite only made it likely.

The fix is that the question remembers how many times the page has changed the
account itself, and its answer applies only if that count still stands. Every
sign-in, registration and sign-out goes through one function that bumps the
count, so the question can tell. There is no timestamp to compare and no
request to cancel, and the test that holds it is three lines: ask, change,
answer, and nothing applied.

```live path=src/lib/auth.ts region=late-answer
```

```live path=src/App.tsx region=who
```

The first fix considered was to make the form wait for the question before it
could submit, which would have hidden the defect behind a spinner and made
every account view a little slower for a race that lasts milliseconds. The
second was to cancel the question when the form submits, which handles the one
order of events that had been seen and not the sign-out that follows a
registration inside the same window. Counting changes handles every order the
same way.

## Addendum, 2026-09-09: the second buyer

Three readers with no memory of the project were handed the checkout on 2026-09-09
and asked to find fault, and two of them, the interviewer and the junior
developer, put the same finding at the top of their lists. It is the question
this record should have asked itself when bids got owners: what happens to the
auction when somebody buys the vehicle? The answer, read off the code, was
nothing. Buy Now recorded the sale on the buyer's own bid,
`WonBuyNow`, and the standing that everybody's overlay reads carried a
`SoldBuyNow` that the room consulted before bidding and nothing else did. So the
buyer's page said Sold, everybody else's page showed a live auction standing at
the Buy Now price, and a second account that bid that price was resolved by the
same shortcut that had sold it the first time and told it had won. Two buyers,
one vehicle, each told the truth as their own bids had it.

That is what "two visitors can outbid each other and both be told the truth"
looks like when one of the truths was never put on the wire. The sale was a
fact about the vehicle, stored as a fact about a bid.

The fix is one boolean, asked in the right place, three times. The rules take
it as a parameter and check it before everything else, including the shortcut:

```live path=api/TheYard.Domain/BidRules.cs region=sold
```

The service supplies it from the standing it already kept, under the same gate
as the write that makes it true, so two purchases cannot both find it false:

```live path=api/TheYard.Application/BidService.cs region=sold
```

And the wire carries it on every vehicle, in every listing and every detail, as
a fact beside the clock's status rather than folded into it, because the browser
recomputes the status from the window as time passes and would overwrite a
status that said otherwise. The parameter has no default, so a caller that
forgets it does not compile:

```live path=api/TheYard.Api/VehicleWire.cs region=sold
```

The browser treats `sold` the way it already treated the buyer's own purchase:
the countdown becomes a Sold chip, the price is labelled a purchase price, and
the panel offers no form. A stranger who had been bidding reads that somebody
bought it.

Three tests hold it, one per layer. The rules refuse a sold vehicle at the Buy
Now price, above it, at the minimum and through Buy Now, and still sell the same
vehicle unsold, so the new check is in front of the old rule and not instead of
it. The service refuses a second account through every door after the first has
bought, records nothing of the stranger's and leaves the buyer holding it, sells
the vehicle the same way when the purchase was a bid at the Buy Now price, and
learns the sale from the store on a replay. And two accounts against the API:

```live path=api/TheYard.Tests/AuthTests.cs region=sold
```

The integration test buys a vehicle other than the one the bidding tests open,
and undoes the purchase at the end with the buyer's own start-over. That is not
tidiness: the test containers on the document store keep every run's bids for a
day (ADR: A second store on Cosmos DB, and what it costs), and a sold vehicle
left there would refuse tomorrow's runs with the sentence this addendum added.
The helper the bidding tests share now skips sold vehicles for the same reason.

What this does not do, and is recorded as open: the sale is still a row on the
buyer's bid rather than a state of the vehicle, so a buyer's start-over
un-sells the vehicle, which is the right answer for a demo and the wrong one for
an auction house; and the check-and-write is serialised by the process's own
gate, which two containers running the same store do not share (ADR: One
container, both stores). Both are the same shape as the retry that overwrites,
and belong to the record that takes that on.

## Files

- [`api/TheYard.Api/Tokens.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Tokens.cs): the token, the cookie, and who is asking.
- [`api/TheYard.Api/Accounts.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Accounts.cs): what a register or login answers with.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the composition, the endpoints, and the three that refuse.
- [`api/TheYard.Application/BidService.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/BidService.cs): the two indexes.
- [`api/TheYard.Application/Ports.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Ports.cs): the store port, now keyed on the pair.
- [`api/TheYard.Infrastructure/YardUser.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/YardUser.cs): the one field Identity does not already have.
- [`api/TheYard.Tests/AuthTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/AuthTests.cs): the proof, and the second buyer refused.
- [`api/TheYard.Domain/BidRules.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/BidRules.cs) and [`api/TheYard.Api/VehicleWire.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/VehicleWire.cs): sold, asked first and said on every vehicle.
- [`docs/ADR-038-identity-explained.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-038-identity-explained.md): the same setup, walked at a new developer's level.
- [`docs/ADR-033-relational-store.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-033-relational-store.md): where bids got somewhere to live.
- [`src/lib/auth.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/auth.ts): the account seam in the browser, and the question that ignores a late answer.
- [`src/lib/auth.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/auth.test.ts): the seam's tests, including the late answer.
- [`tests/e2e/account.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/account.spec.ts): the form end to end, the run that found the late answer.
