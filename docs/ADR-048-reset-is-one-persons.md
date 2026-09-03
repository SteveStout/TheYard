# ADR: Reset is one person's start-over

Status: accepted, 2026-09-03. `DELETE /api/bids` took no user and deleted
everybody's rows. It was documented, deliberate, and had quietly stopped being
defensible two versions earlier.

## What it did

The demo has a "Reset bids" button so a visitor can start the auction over. The
endpoint behind it:

```csharp
app.MapDelete("/api/bids", (BidService bids, MarketService market) =>
{
    bids.Reset();
    market.Reset();
    return Results.NoContent();
}).RequireAuthorization();
```

No user parameter. `BidService.Reset()` cleared `_standing`, `_byUser` and the
store, and `EfBidStore.Clear()` was `db.Bids.ExecuteDelete()`: every row in the
table, for everybody.

The behaviour was not an accident. The method carried a comment defending it:

> The demo's start-over button. It clears everybody, not just the caller, because
> the room's bids (ADR-027) are shared and a reset that left half of an auction
> standing reads as a bug however carefully it is explained.

That reasoning was correct when it was written. At the time a bid belonged to a
browser session, there was one visitor by construction, and "everybody" meant
"you".

## When it stopped being correct

1.0.0.48 gave bids owners. 1.0.0.49 put them in Azure SQL Database. From that
point:

- Any signed-in visitor could delete every other visitor's bids.
- The deletion was durable, because the store went with it.
- And this repository's own changelog says of 1.0.0.48: *"two visitors on the
  live site can outbid each other and both be told the truth about it"*, which
  the reset endpoint made false.

An authenticated user destroying another user's data is broken access control,
and it is the first item on the OWASP top ten. On a portfolio demo where the data
is invented bids it costs nothing. As a thing a reviewer reads in a repository
that spends forty-seven records arguing about care, it costs a great deal.

The comment is the interesting part. It is not a case of nobody thinking about
it. Somebody thought about it, wrote down a good reason, and the reason expired
without the sentence changing. That is harder to catch than an absent thought,
because the file reads as though the question has been settled.

## Decision

The reset belongs to the caller. The original reasoning survives, narrowed to
the vehicles the caller actually touched.

```csharp
public IReadOnlyList<string> Reset(string userId)
{
    if (!_byUser.TryRemove(userId, out var mine)) return [];
    string[] touched = mine.Keys.ToArray();
    _store.Clear(userId);
    foreach (string vehicleId in touched) { /* recompute the standing */ }
    return touched;
}
```

Three things it does, and one it deliberately does not.

**It returns what it touched, and the endpoint hands that to the room.** The
room's counter-bids on those vehicles go with the caller's, which is the whole of
the original argument: a reset that takes your bid and leaves the room's answer
standing reads as a bug. That argument only ever needed to apply to the vehicles
the person bid on. `BidService` does not reach into `MarketService`; it reports,
and the composition root connects them.

**It recomputes each vehicle's standing rather than deleting it.** Deleting the
standing would hand the vehicle back to its opening ask, which quietly discards a
third person's bid on the same car: the same defect, one size smaller. Instead
the top remaining bid across the other users becomes the new standing, so a
stranger who bid on that car keeps their bid and keeps the lead they earned. The
scan is over users and their bids, which is small, rather than over the hundred
thousand vehicles, which is not.

**The store deletes one person's rows**, `WHERE UserId = @userId`, rather than
truncating the table.

**It does not try to restore the room's earlier bid on those vehicles.** The room
answers bids; with the buyer's bid gone there is nothing for its earlier answer
to be a reply to. Removing it is the honest state, and it is what the original
decision chose too.

## What the tests hold

The one that matters names the situation rather than the mechanism:

```csharp
[Fact]
public void Reset_leaves_a_stranger_bidding_on_the_same_vehicle_alone()
{
    service.PlaceBid(vehicle, 23_300, Now, Buyer);
    service.PlaceBid(vehicle, 24_000, Now, "somebody-else");

    service.Reset(Buyer);

    Assert.Empty(service.SnapshotFor(Buyer));
    Assert.Single(service.SnapshotFor("somebody-else"));
    Assert.Equal(24_000, service.Apply(vehicle).CurrentBid);
}
```

Both halves: the caller's bid is gone, and the stranger's is untouched and still
leading. A test asserting only the first would pass on the old behaviour.

## What it broke, which is the interesting part

`smoke.spec` failed on the first run after this change, and on the second, on the
assertion right after it places a bid. Two runs is not a flake, and the change
was mine, so the question was what it took away.

`smoke.spec` reads the minimum next bid off the field's placeholder and posts it,
once, with no retry. The room raises prices every eight seconds, so that has
always been a race: read a number, and by the time the click lands the number can
be stale and the server is right to refuse it. `market.spec` hit exactly this and
was given a helper that reads the number again when the server says no.

`smoke.spec` never needed one, and now it is clear why. `market.spec`'s reset
used to clear the room **globally**, so through most of a suite run there was
nothing bidding against anybody, and `smoke.spec` was posting into a quiet field.
Scoping the reset to one user left the room's other bids standing, which is
correct, and the race that was always in `smoke.spec` started happening.

So the test was passing because a different test kept clearing the world.

The helper moved to `tests/e2e/bidding.ts` and both specs use it. Two tests
solving the same problem two ways, one of them by accident, is worse than either
answer.

This is the second time this kind of dependency has surfaced here, and the shape
is worth naming: **a test that passes because of something another test does to
shared state is not a passing test**, and it looks exactly like a passing test
until the day somebody fixes the thing it was leaning on. It also means the fix
looks like the cause, which is how a correct change gets reverted.

## Consequences

- Two visitors on the live site can now do what the changelog has been claiming
  they could do for twelve versions.
- The reset button still does what it appears to do for the person pressing it,
  including clearing the room's replies to them.
- `IBidStore.Clear` takes a user id, so an implementation cannot truncate the
  table by accident.
- One narrow behaviour change to the demo: pressing reset no longer returns
  every vehicle on the site to its opening ask, only the ones you bid on. That is
  what the button always claimed to do.

## Files

- [`api/TheYard.Application/BidService.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/BidService.cs): the reset, and the recompute.
- [`api/TheYard.Application/MarketService.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/MarketService.cs): `Forget`, which takes the vehicles rather than clearing the room.
- [`api/TheYard.Application/Ports.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Ports.cs): the port that now needs a user.
- [`api/TheYard.Infrastructure/EfSources.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/EfSources.cs): one person's rows.
- [`api/TheYard.Tests/BidServiceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/BidServiceTests.cs): the stranger who keeps their bid.
- [`docs/ADR-037-accounts.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-037-accounts.md): the change that made this wrong.
