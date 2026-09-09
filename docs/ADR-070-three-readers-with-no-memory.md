# ADR: Three readers with no memory of the project

Status: accepted, 2026-09-09. Steve's ask, at the end of a day of shipping:
"before you stop, remove all context and look at our code, and play devil's
advocate as if you were an interviewer, a junior developer or an architect
learning from or reviewing my work". The review was run that afternoon, its
findings were verified against the code, and the decisions taken on each are
this record. Three versions carry them: 1.0.0.109, 1.0.0.110 and 1.0.0.111.

## Context

Every review this project had run before was made by the session that wrote
the code, or by a session that had read every record first. Both know what
the system is meant to do, which is exactly the knowledge that hides what it
does. The ask was for the opposite: readers who would meet the checkout the
way a stranger meets it.

Three were run, as separate agents with no memory of any session, each given
the checkout at 1.0.0.108 and one persona. A hiring manager screening the
repository before a final round. A junior developer a year into a first job,
told to learn from it. A principal architect deciding whether it becomes a
team's reference. None was told what the others were asked. Their reports
are kept outside the repository; every claim in them was then read against
the code by the session that had written it, and the ones that held are
below, in the order of what they cost.

The verdicts, in one line each. The interviewer would advance the candidate
on the strength of the process, on condition the interview tests whether the
candidate understands the auction the code implements rather than the one
the records describe. The junior developer learned Cosmos DB, the onion and
the test shapes from it and would copy six things, and found four documents
giving four different answers to where a bid goes. The architect would keep
the ports, the schema authority, managed identity and the readiness
discipline, and strike four things before anyone copied them: authoritative
state in process memory in front of two writers, an auction clock the caller
chooses, an anonymous write on the Admin tab, and a signing key each process
invents.

## What they found, and what was decided

Eight findings held. Each got one of three answers: fixed, with the version;
decided and designed, with the version it ships in; or accepted as it is,
with the reason.

**1. A purchase sold the vehicle to its buyer only.** Two of the three put
this first. Buy Now recorded the sale on the buyer's own bid and nowhere the
rules or the wire could see it, so a second account saw a live auction at
the Buy Now price, bid it, and was told it had won the same vehicle. Fixed in
1.0.0.110: the rules take `sold` and check it before everything, the bid
service supplies it from the standing it already kept, every vehicle on the
wire says `sold`, and the page shows Sold to strangers (ADR: Accounts and
per-user bids, the addendum on the second buyer).

**2. The auction clock is decided by the caller.** The page sends its own
local midnight as `anchor_ms`, the server derives every window from it, and
the schedule record and the CLAUDE.md rule both describe that as the design.
So two visitors in different zones are in different auctions, and a client
that sends yesterday's midnight bids on a vehicle that ended for everyone
else. Decided here: the clock is the server's. The anchor becomes the
current UTC day's midnight, computed per request on the server and sent to
nobody; `anchor_ms` leaves the API and the page; the windows re-seed at
00:00 UTC rather than at each visitor's midnight, which on the live site is
seven in the evening for its owner; and the browser keeps doing what it does
now, formatting the instants the server sends and counting down to them. One
auction, one clock, and no request can name a day. It ships as its own
version with the tests that hold it, and this record gets the addendum; until
then the CLAUDE.md rule stands as written.

**3. Starting the performance proof was anonymous and registered two
accounts a run.** With the one-minute cooldown, one request a minute spent
the whole hour's allowance of registrations, so a stranger could stop real
visitors registering; and the limiter took its slot before Identity had
validated the request, so failed registrations spent it too. Fixed in
1.0.0.109: starting a run takes a signed-in visitor, the card says so, the
proof's two accounts are made once per process and reused, and a slot the
identity refuses goes back (ADR: The one write a stranger can make and ADR:
Same performance, proven, their addenda).

**4. The signing key was invented at process start and nothing configured
it.** Every roll signed every visitor out of a seven-day cookie, and the
comment and the accounts record said the process warned when it did, which
no log line did. Fixed in two halves. 1.0.0.109 added the warning. 1.0.0.111
makes the key configuration the deploy hands to both containers from one
repository secret, `YARD_AUTH_SIGNING_KEY`, substituted into the container
spec at roll time the way the connection strings are, so a session survives a
roll and a token minted by one container reads on the other. A roll with no
secret keeps the placeholder, and a placeholder, an empty value or a key too
short to sign with all count as no key: the process invents one and says so,
which is every roll's behaviour until the secret exists. The only secret in
the pipeline, masked by GitHub in every log, never read by anything but the
roll.

```live path=api/TheYard.Api/Tokens.cs region=configured-key
```

**5. The retry that overwrites.** Both bid stores catch a concurrency
conflict on the buyer's row and write the same intended state again; the
comments in both adapters and in the DDL said the answer was to start again
from what was there, and the rules were not re-run. With two containers
writing one store, and the header making a cross-container write routine,
that is how two buyers could hold one vehicle. Two things were true of it on
reading. The row a conflict guards is one buyer's, so the race it catches is
the same account bidding from two containers at once, and re-applying the
later bid is a defensible answer to that. And two buyers racing each other
are two rows, so the token never sees that race at all: what they race for
is the vehicle's standing, which lives in each container's memory. 1.0.0.111
makes the three comments say exactly that. The design that closes the gap is
decided here and not yet built: a standing per vehicle that the store owns,
one row or document carrying the amount, the count, the high bidder, the
sale and a version; a bid writes the buyer's row and then the standing with a
compare-and-set on the version; a conflict re-reads the standing and runs the
rules against it before a second attempt, so a bid that no longer clears the
minimum is refused with the sentence it would have got a moment later; and
the in-memory index becomes what it should have been, a cache of the store's
standing, refreshed on conflict. On the relational store that is one
transaction; on the document store the buyer's document and the standing
live in different partitions, so it is two writes and a repair on replay. It
is a schema change on both stores and a record of its own when it ships.

**6. Documents describing systems that no longer run.** All three tripped on
it first: an architecture page saying no authentication and one SQLite file,
a data flow page with one anonymous buyer, CLAUDE.md with five projects and
decimal money, a README calling the site free and putting accounts out of
scope, and a dozen smaller ones across the records. Fixed in 1.0.0.109, and
the README's own bidding paragraph, which that pass missed, in 1.0.0.110.
The live-sample mechanism protects fenced code and nothing else, which is the
honest reason prose drifted while the code in the same pages could not.

**7. Numbers that inflate.** The search claim credited a test with a
stopwatch's number; the test asserts only that the indexed path is not
slower. Fixed in 1.0.0.109, with the weaker claim the test makes. The
interviewer's count that about a quarter of the xUnit tests test the
documentation apparatus rather than the product is accepted as a fact about
this repository, and left as one: those tests are what keep the pages the
site serves from lying, which is the failure the three readers found first.

**8. The smaller ones.** A `Lazy` kept a failed background warm for the life
of the process while the host promised a retry: fixed in 1.0.0.109, the next
caller retries (ADR: The ports learn to wait, addendum). A request that
reached a store whose catalogue was still loading blocked its thread on the
load, the shape that record argued against and said the host did not do, and
did do for the seconds after a roll before the other store had warmed and
for every request to the other store in a test application: fixed in
1.0.0.111, the pipeline awaits the warm before any endpoint reads the store.

```live path=api/TheYard.Api/Stores.cs region=warm-before-reading
```

A session opened on one store could bid on the other, because the header
puts one request on the other store and nothing checked that the account
was there: the relational store's foreign key answered with a 500, the
document store with nothing. Fixed in 1.0.0.111: the token names the store
that opened it, and the two bid writes refuse a session from another store
with a sentence, at the cost of no lookup.

```live path=api/TheYard.Api/Tokens.cs region=session-per-store
```

A reset cleared memory before it asked the store, the opposite of the bid
path's rule, so a store that refused left the page saying the bids were gone
while the rows came back at the next start: fixed in 1.0.0.111, the store
first.

Accepted as they are, each with its reason. Identity's lockout count can
lose an increment when two failed sign-ins on one account race, on either
store; that is the framework's shape, the lockout still trips, and a
per-account brute force has to be parallel against itself to gain by it. The
SQL and store logs live in the Application project although their vocabulary
is a store's; they are the application's own record of what its ports did,
and moving them would move the Admin tab's reading of them too. The smoke
suite registers an account per test, which is isolation by construction and
spends a handful of the hour's allowance on a machine that is not the live
site. And an account deleted
while its cookie is still valid can still post a bid that the relational
store's foreign key refuses as a 500, which the page cannot reach because
`/api/auth/me` reads that account as signed out; named here rather than
fixed, because the fix is the same account lookup per bid that the store
claim was chosen to avoid.

## What did not hold, so the next review knows the shape

Little. One number in the verification pass was wrong rather than any of
theirs: it said the proof's tolerance would let a 12 ms row and a 29 ms row
read the same, and it would not; the allowance is fifteen milliseconds or
fifteen per cent of the slower row, whichever is more, which is fifteen here
against a difference of seventeen. The architect's wider point stands, that
eight samples a path and a correction that subtracts one round trip per
operation assume much of what they conclude, and the proof record says as
much about throughput and contention. Nothing any of the three called broken
was found to be working.

## Consequences

- A stranger's reading of the checkout is now a documented step, not a
  hope. Three of the four things the architect would strike are gone or
  designed; the fourth, the clock, is decided above.
- The wire carries two new facts, `sold` on every vehicle and a `store`
  claim in every token, and both are refusals the server makes rather than
  facts the browser is trusted with.
- One secret exists in the pipeline where none did. It is a session key, it
  lives in GitHub's secret store and the container's environment and
  nowhere else, and a roll without it degrades to what every roll did before.
- Two designs are owed and named: the server's clock (finding 2) and the
  standing the store owns (finding 5). Until they ship, the rules against
  re-implementing auction math in the browser and against a second writer
  are what they were, and this record says so plainly rather than the
  older records pretending otherwise.

## Files

- [`api/TheYard.Api/Tokens.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Tokens.cs): the configured key's rule, the store claim, and the session-per-store check.
- [`api/TheYard.Api/Stores.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Stores.cs): the warm before any read.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the key from configuration, the warming middleware, the bid endpoints' refusal.
- [`api/TheYard.Application/BidService.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/BidService.cs): the reset, store first.
- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard.yaml), [`infra/aci-theyard-cosmos.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard-cosmos.yaml), [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml) and [`.github/workflows/deploy-cosmos.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy-cosmos.yml): the one secret, substituted at roll time.
- [`api/TheYard.Tests/WarmthTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/WarmthTests.cs), [`api/TheYard.Tests/AuthTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/AuthTests.cs) and [`api/TheYard.Tests/BidServiceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/BidServiceTests.cs): the three holds.
