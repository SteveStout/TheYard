# ADR: Automated tests never touch a real database or permanent storage

Status: accepted, 2026-11-20, at 1.0.5.131. Steve set the rule in two steps: on 18 November, "Generally speaking automated tests should never make permanent changes to data" and "when possible use a mock database"; on 19 November, "all of our tests should use mock when they can ideally automated tests never touch the databases or permanent storage". This record is ADR-141.

## Reading list

1. [ADR: The gate runs where the memory is](https://theyard.stevenstout.biz/?doc=adr-the-gate-runs-where-the-memory-is): its addendum of 1.0.5.94 moved the gate's Cosmos DB passes onto the emulator, the first step of this rule.
2. [ADR: A second store on Cosmos DB, and what it costs](https://theyard.stevenstout.biz/?doc=adr-second-store): why the store tests fail, never skip, when no store is named.
3. [ADR: The rules a change has to pass](https://theyard.stevenstout.biz/?doc=adr-rules): the table this rule's test is listed in.

## In plain words

A test proves the code, not the world. So a test reads and writes a stand-in: first a fake of the port the code talks to, held in memory; and only when a fake cannot prove what is being tested, a store born with the run and thrown away with it. It never reads or writes the database the sites read, a storage account, the job-hunt share, Gmail, or anyone else's service. A test that did would leave rows behind, spend the live account's throughput while visitors are on it, and turn red when the world changes rather than when the code does.

## Context

Until 1.0.5.94 the gate ran its Cosmos DB passes on the live account's tests- containers. Each pass spent about 950 RU/s of a 1,000 RU/s database the sites share, brought thousands of 429s to every gate window, and left 1.98 million test documents (2.56 GB) in tests-logs. 1.0.5.94 moved those passes onto the Cosmos DB emulator, a container started beside the gate's job and gone when it ends. Steve's words above widened that from the gate to every automated test.

An audit on 19 November (code1118e, read only) read every xUnit, Vitest, Playwright and Functions test for what it touches. Nearly all of them already use a fake: the API's test hosts boot a scratch SQLite file and delete it, every outside reader in a test is routed to a recorded answer, the Playwright specs run against a local server, and the Functions and server suites read and write in-memory shares and a scripted Gmail. Three places did not hold, and they are listed below.

## Decision

1. **Mock first.** A unit or handler test uses an in-memory fake of the port: the repository, the store, the blob, the share, Gmail, Hunter, LinkedIn. That is the default for every new test.
2. **A throwaway store only when a fake cannot prove it.** A test that must prove a store's own behavior (a query, an index, paging, a transaction) runs on a store born and thrown away with the run: SQLite in memory or in a temp file, or the Cosmos DB emulator container. It says why in one line.
3. **Never the live ones.** No automated test reads or writes cosmos-theyard-ss, the Azure SQL database, any storage account (stjobhuntss included), the job-hunt share, Gmail, or any outside API. A read after a roll is a smoke check of production, not a test, and writes nothing.
4. **A new test that breaks this is a defect** and does not ship. The exceptions that exist today are listed here with a plan each; none is added.

### The exceptions, and the plan for each

| Where | What it touches | Plan |
| --- | --- | --- |
| `CosmosKeysetTests` (api/TheYard.Tests) | Reads the live catalogue once in the gate's cosmos job, about 2,000 RU, to hold a deep page under 100 RU; the emulator reports no request units. Read only. | Seed a catalogue on the emulator (`scripts/cosmos_emulator.mjs`), keep the test's row-equality and no-OFFSET checks there, and hold the cost by the query the store's log shows rather than by request units. Then the cosmos job drops `azure/login` and the gate environment, and `GateWorkflowTests` stops pinning the one live address. |
| `CosmosLive` (api/TheYard.Tests/CosmosStoreTests.cs), shared by the store tests | Writes. In the gate it is the emulator; on a laptop it was whatever `Cosmos__AccountEndpoint` named, and nothing stopped that being the live account. | Done in this version: it refuses any address that is not this machine, and its credential defaults to the emulator's. |
| `ParityTests` and `TextAppendMergeTests` (functions/jobhunt.Tests) | Read a copy of the real tracker CSV and markdown files named by `PARITY_DATA` and `PARITY_MD_DATA`, on a Windows laptop only; unset, they return without running. Never in the gate. | Replace them with a synthetic tracker checked in beside the tests (the `Seed` helper already builds the edge rows), and keep the PowerShell comparison as a tool run by hand, not a test. |

## What holds it

`TestsTouchNoLiveStoreTests`: the store fixture refuses a non-loopback address (planted: the live account's address, refused by name); no test file opens a Cosmos DB store but the guarded fixture and the listed exception; and every share or blob client a test builds rides a fake transport. Planted before the guard: an unguarded fixture failed two of the three. The gate's own workflow is held by `GateWorkflowTests` (one live address in the gate, the keyset read's, and `azure/login` in no other job).

## Consequences

- A test's red means the code changed, not the account, the share or a mailbox.
- The gate spends none of the live account's throughput but the one listed read, and leaves nothing behind.
- What stops being true: a laptop run with a live endpoint set could write the account's tests- containers. It now stops before a client is made.
- What stays true: the store tests fail, never skip, when no store is named; a smoke read of production after a roll is still how the live sites are checked.

## Where it sits

Outside the rings: a rule over the test suites, held by a test that reads them, beside the rules in ADR-075's table.

## Files

- [`api/TheYard.Tests/TestsTouchNoLiveStoreTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/TestsTouchNoLiveStoreTests.cs) - the three checks
- [`api/TheYard.Tests/CosmosStoreTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/CosmosStoreTests.cs) - `CosmosLive`, the store tests' one fixture, and its guard
- [`api/TheYard.Tests/CosmosKeysetTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/CosmosKeysetTests.cs) - the listed exception
- [`.github/workflows/gate.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/gate.yml) - the cosmos job and the emulator beside it
