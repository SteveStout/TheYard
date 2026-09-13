# Built with AI

TheYard was built by one engineer using AI as a force multiplier. This page says what that meant in
practice: what the AI wrote, what I decided, what governed the result, what it cost, and what went wrong.
Everything here links to the record or the file that proves it.

## What the AI wrote

The first draft of most of the code, most of the decision records, the deployment scripts and the tests.
Commits carry the co-author trailer and the repository carries CLAUDE.md, the standing instructions the AI
worked under. None of that is hidden and no history has been rewritten.

## What I decided

The choices a tool cannot make, each with its record.

- The domain and the fresh repository, dropping a take-home's lineage for a platform I own.
- The hosting path under a free-trial subscription: Container Instances behind an edge for TLS now, with
  App Service and Front Door designed and deliberately undeployed (HOSTING.md, ADR-006, ADR-007).
- A second store on Cosmos DB beside Azure SQL, the same image on both, and the bar it had to clear: as
  fast as SQL Server on a different data structure at the lowest cost (ADR-058, ADR-059).
- One container running both stores with a store toggle at the top of every page, then a permanent address
  for the second site (ADR-066, ADR-069).
- The sidebar over dropdowns, the palette, and the rule that every diagram opens on its own page
  (ADR-013, ADR-016, ADR-020).
- The five-minute ceiling on the whole test wall (ADR-068).
- Every GO. Nothing rolled to Azure without a written go from me.

## What governed it

**The test gate.** Every push runs all three suites in CI: 454 xUnit tests, 102 Vitest tests at 1.0.0.123 and 62 Playwright tests. The ship
gate runs the API suite against both stores and was measured at 275 seconds green on a quiet machine, 302
to 342 seconds with the developer's browser open, and 372 seconds cold after a restart (ADR-068). A push that fails the gate does not roll.

**The records.** Seventy-three decision records, each carrying the decision, the trade-off and the number behind
it, with a Files section pointing at the code it governs. Code shown in a record is read from the running build, so a
record cannot drift from the code it describes.

**The reviews.** A self review before one ship found eleven defects, four of them serious: a bid acceptable
below the going rate, a lost update on concurrent bids, a poll erasing a just-placed bid, and the simulated
room hammering three cars (ADR-027). Later, three readers with no memory of the project, an interviewer, a
junior developer and an architect, reviewed the checkout cold, and every finding got a decision: fixed with
its version, designed with the version it ships in, or accepted with its reason (ADR-070).

**The measurements.** On 3 of 8 paths the two stores answer in the same time. On the other 5 the difference
is the round trip to the store, 39 ms to Azure SQL Database against 2 ms to Azure Cosmos DB, and taking one
round trip per operation off each side leaves them the same (ADR-067). A sign-in costs 2.00 request units, a
bid write 6.52, a raise 11.29, a registration 13.04 (ADR-064, ADR-059). Leaving the indexing policy at the
default cost 16.07 request units a document on the bulk seed against 8.84 tuned, 21 minutes against 13, and
8,407 documents refused by throttling that the tuned seed never saw (ADR-065).

## What it cost

- The Cosmos DB account on the free tier: $0.00 a month, 1000 RU/s shared, with local auth disabled so no
  key exists (ADR-058, ADR-059).
- The second container group for the comparison: about $34 a month at list price while it runs, drawn from
  trial credit and stopped between comparisons (ADR-059).
- The edge: Netlify's free plan allots 300 credits a month, and 11 production deploys had consumed 165 of
  them at 15 each while serving the site cost almost nothing. Application pushes no longer redeploy the
  edge, so they cost zero credits (ADR-007).

## What went wrong

- **1.0.0.90 took the live site to files.** A transitive package bump broke managed-identity token
  acquisition for SQL inside the container. The Azure SQL container came up on files twice, once on the roll
  and once on a restart, against a database that was demonstrably online, with a `SqlException: A task was
  canceled` wrapping a `TaskCanceledException` in the Azure retry pipeline both times. Rolled back by hand,
  the package pinned with the reason in the project file, and the lesson recorded: pin shared Azure packages
  across projects (ADR-059 addendum).
- **Warming both catalogues before serving turned a two-minute suite into a thirty-minute crawl** that
  looked like a hang. Only the default store warms before serving now (ADR-066).
- **A per-commit changelog line once invented a version that never shipped**, because two commits pushed
  together are one deploy. The ship gate reads the live run number now (ADR-012 addendum).

## How to check any of this yourself

- Open the Admin tab on either site: live health checks per store, the paired-round comparison card, the
  last container events, and the log of every store operation.
- Open any record: the Files section links the code it governs, and the live blocks are read from the
  container's own source at request time.
- Run the gate yourself, or read the CI runs linked from CICD.md.
- Read the Performance page beside this one for what the measurements add up to on the smallest machine that will hold it.

## Files

- [`CLAUDE.md`](https://github.com/SteveStout/TheYard/blob/main/CLAUDE.md): the standing instructions the AI worked under, kept in the repository rather than hidden.
- [`.github/workflows/ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml): the three suites on every push, and the coverage annotation. The jobs, read from this build:

```live path=.github/workflows/ci.yml region=ci-jobs
```

- [`api/TheYard.Api/DocsCatalog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/DocsCatalog.cs): every document this page sits beside, served from the checkout by slug.
- [`api/TheYard.Tests/PublicFaceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/PublicFaceTests.cs): the test that holds the record count in every living document, this page included, and the README's test counts to what the suites declare.
- [`docs/ADR-068-the-five-minute-gate.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-068-the-five-minute-gate.md), [`docs/ADR-027-competing-bidders.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-027-competing-bidders.md), [`docs/ADR-070-three-readers-with-no-memory.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-070-three-readers-with-no-memory.md), [`docs/ADR-067-same-performance-proven.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-067-same-performance-proven.md), [`docs/ADR-059-a-second-store-priced.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-059-a-second-store-priced.md): the records the numbers above come from.
