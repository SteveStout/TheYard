# ADR: The one write a stranger can make

Status: accepted, 2026-09-03. The security page has said there is no rate
limiter since it was written. This is not the limiter it meant, and that is the
point.

## Context

Ask a narrow question instead of a broad one: what can somebody with no account
cause this application to keep?

- `GET` everything. Reads, and they cost a query.
- `POST /api/errors/client`. Writes into a ring buffer with fifty slots, each
  one bounded in size, and the ring is the browser's own so a flood of them
  cannot push server errors out of the operator's view. That was fixed in
  1.0.0.64 and it is the shape this record copies.
- `POST /api/auth/register`. Writes a row to `AspNetUsers`, in Azure SQL
  Database, and keeps it.

That is the whole list, and only the last one persists. Bids need an account,
and a bid is keyed on the person and the vehicle together, so a signed-in
visitor can hold at most one row per vehicle. Which means the number of rows
anybody can add to this database is a function of the number of accounts, and
the number of accounts had no ceiling at all.

The rows are the smaller half. Identity hashes a password on the way in, on
purpose, at a cost measured here at about 120 ms of CPU. One container serves
this site. A loop against this one endpoint is a hundred and twenty milliseconds
of somebody else's CPU per request, which is a better attack on this site than
anything else it exposes, and it needs no account, no session and no cleverness.

## What the security page actually argued

It says:

> Behind the edge, the origin sees one address for every visitor, so an
> IP-partitioned limit is a global cap rather than a per-attacker one, and
> because the origin is directly reachable an attacker can bypass the edge and
> forge whatever address they like.

Every word of that is true and none of it is about this. It is an argument
about **partitioning**: any limit that has to tell one visitor from another is
either lying to itself behind the edge or being lied to in front of it.

A limit that does not partition has nothing to forge. It counts registrations,
full stop, and an attacker with ten thousand addresses gets exactly the same
answer as an attacker with one.

That distinction was available the whole time and was not made, because "there
is no rate limiter" was written once and then read as settled. It is the same
species as the three findings in 1.0.0.61 through 1.0.0.63: a sentence that was
correct about the question it was answering, left standing over a question it
was never asked.

## Decision

A sliding hour, counted across the whole site, of 120 registrations.

**Sliding rather than a bucket that resets on the hour.** A bucket hands the
whole allowance back at a known instant, so an attacker takes it twice in two
minutes by arriving either side of the reset. The window holds the times of the
last accepted registrations and ages them out one at a time.

**A refusal is not recorded.** Only an accepted registration takes a slot. If
refusals counted, fifty of them in the fifty-ninth minute would hold the window
open for another hour, and a limiter that never lets go while anybody keeps
knocking is the outage it was built to prevent.

**Checked after the request is read and before the password is hashed.** A
malformed request should not spend the allowance, and a refused one should not
spend the CPU, which is the thing being protected.

**The refusal says nothing with a number in it.** It says the demo is not taking
new accounts and to try in an hour. A reply that reported how much of the
allowance remained would be a counter anybody could poll, and the test asserts
no digit reaches the words.

**120.** This site has never seen more than a handful of registrations in a day
and most of those were its own tests, so the ceiling sits three orders of
magnitude above real use. Nobody meets it who is not trying to.

## What this costs, said plainly

While an attacker is spending the hour's allowance, a real visitor cannot
register either. That is a denial of service, in the small, and it is the trade
being made on purpose: an hour of no new accounts is cheaper than an unbounded
bill and a saturated container, and everything else keeps working. Browsing,
signing in, bidding, and every existing account are untouched, and one test
exists only to say so.

The honest version of the alternative is worth writing down: with no limit, the
same attacker gets the CPU and the rows and nobody is refused, because there is
nothing to refuse them with.

## Alternatives

**A total cap on accounts.** Simpler, and it bounds the bill forever rather than
per hour. It also converts a temporary refusal into a permanent one: fill it
once and this site never takes another account without a deploy. For a portfolio
whose entire purpose is that a stranger can try it, that is the wrong failure to
choose.

**Per-address limiting.** The security page explains why this does not work
here, and it still does not.

**A CAPTCHA.** It would work, and it would put a third party's script on a page
whose whole argument is that a reader can see everything it does.

**Nothing, and say so.** This was the position until today and it was defensible
while the origin held no data. Accounts and a database arrived underneath it.

## What is still owed

The window lives in memory, in one container. A second instance would get its
own, and the site's real ceiling would be 120 times the instance count. That is
correct for what runs today, one container, and it is written here rather than
discovered later. A durable counter belongs with the origin lock, which needs a
paid subscription, and those two are one piece of work.

## Consequences

- The only anonymous write this application accepts has a ceiling, and so, by
  the key on the bids table, does everything else in the database.
- A visitor who has never seen it will never see it.
- The security page's rate limiter paragraph is now half true, and its addendum
  says which half.

## Files

- [`api/TheYard.Api/RegistrationLimit.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/RegistrationLimit.cs): the window, and why it slides.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): where it is taken, between reading the request and hashing the password.
- [`api/TheYard.Tests/RegistrationLimitTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/RegistrationLimitTests.cs): the window, the refusal, and the site that keeps working behind it.
- [`docs/SECURITY.md`](https://github.com/SteveStout/TheYard/blob/main/docs/SECURITY.md): what is protected, what is not, and what this changes.
- [`docs/ADR-050-a-password-guess-should-cost-something.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-050-a-password-guess-should-cost-something.md): the other half of making an endpoint cost something.
