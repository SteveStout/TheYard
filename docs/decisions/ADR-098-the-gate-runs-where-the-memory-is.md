# ADR: The gate runs where the memory is

Status: accepted, 2026-10-13 (1.0.3.123). The ship's gate runs on a GitHub runner instead of the laptop that ships. The laptop builds the commit and pushes it to a branch named `gate/<version>`; `.github/workflows/gate.yml` runs every check the laptop's gate ran; the laptop reads the run and, only when it is green, fast-forwards main, which deploys as before.

## In plain words

Every version of this site goes through one gate before it rolls: every suite, on both stores, once (ADR: The five-minute gate). Until this record the gate ran on the laptop that ships, which has 8 GB and not much of it free with an editor open. The gate now runs on a GitHub runner with 16 GB, and the laptop only builds the commit and reads the answer.

What that is worth: a change is tested on the day it is made instead of when the laptop happens to have memory to spare, and the organization keeps one gate with every check in it, on a machine sized for it, at no cost on a public repository.

## Context

The laptop's gate waits for memory before it starts, because starting short of it is how gates went red on specs the change never touched: takes one to three of 1.0.0.187 started with 807 to 1,402 MB free and timed out, and take four waited three hours for memory and never started. On 13 October the gate for 1.0.3.121 waited from 10:42 to 12:03 with between 323 and 1,189 MB free, and the laptop restarted under it. The same day, with VS Code, the API under the debugger, Vite and Chrome open, the laptop had 429 MB free. Steve, that morning: "think around this memory bottleneck, this laptop is small and that constraint can't change."

Three ways were weighed. Make the laptop's gate fit (one browser worker, no parallel test collections, a lower start threshold): it starts sooner and runs slower, and the next suite added pushes it back over. Close what holds the memory during a ship (the C# language server, Docker Desktop): cheapest, and manual every time. Move the gate to where the memory is: GitHub's standard runner for a public repository has 16 GB and four cores and costs nothing, and CI already runs this repository's suites there on every push to main (ADR: The five-minute gate, the addendum of 5 October).

## Decision

**The checks move; none is dropped.** The workflow runs, in the laptop's order: the build of every project with any warning red, the bundle and the rendering service's bundle, dotnet format, the SQL project, xUnit on SQLite, the seven live Cosmos DB tests, the whole xUnit suite booted on Cosmos DB, Prettier, oxlint, the type check, Vitest, the browser suite against the bundle just built, the three store specs on Cosmos DB, and the em dash scan. The two cuts stay as they were: dotnet format runs when a C# or project file changed, and the two Cosmos DB passes run when `api/`, `infra/cosmos/` or a store spec changed and are carried forward otherwise. The Shed's two suites run beside it on a Windows runner, because its path rules are written for the system they run on and one of its cases (a drive letter makes a path rooted) only means something on Windows.

```live path=.github/workflows/gate.yml region=two-cuts
```

**The run cannot push.** The workflow reads the code and signs in to Azure for the Cosmos DB passes; it has no permission to write to the repository. Its reports leave as one artifact, `gate-raw`, in the layout `scripts/test_results.mjs` already reads. The laptop downloads it, writes `data/test-results.json` and the README lines from it into the same commit, and pushes that commit to main as a fast-forward. A red run leaves main exactly where it was and the commit on its gate branch for reading. Because the push to main is the laptop's own, the deploys start on it as they always have; a push made by a workflow's token would not start them.

```live path=.github/workflows/gate.yml region=verdict
```

**Signing in without a secret.** The Cosmos DB account has no keys, so the passes sign in as an identity. The workflow runs in a GitHub environment named `gate`, and the deploy identity carries a federated credential for that environment and a data role on the one Cosmos DB account, so no branch name is registered in Azure and no secret is stored in GitHub. The token GitHub presents names the owner and the repository by their ids as well as their names, and the credential's subject has to match that spelling.

**The workflow is held to the gate it replaced.** `GateWorkflowTests` names every check with the command that runs it, reads `scripts/test_results.mjs` for the reports it expects and fails when the workflow writes one fewer, requires every result the workflow records to feed the verdict, holds The Shed's job to Windows, and holds the workflow to `gate/` branches and to read-only access.

```live path=api/TheYard.Tests/GateWorkflowTests.cs region=every-check
```

**Measured on the first trial run** (run 38470810828, 13 October, no Cosmos DB sign-in yet): the browser suite on SQLite 189 seconds against 329 to 390 on the laptop that day; Vitest 4 against 10 to 11; xUnit on SQLite 17 against 25 to 31; the whole run 23 minutes, ten of them the Cosmos DB passes waiting on a sign-in that failed. The laptop's part is the patch, the drawings and the push: light work, in memory any laptop has free.

## Where it sits

Outside the rings: the build machinery, read by nothing at run time. Single responsibility on both sides: the workflow answers whether a commit is green, and the laptop's script answers what ships and when. It costs a wait on GitHub's queue and a run that is slower to start than a local one; what would change it is a laptop with the memory to run the suites beside an editor, or a check that needs something only the laptop has.

## Files

- [`.github/workflows/gate.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/gate.yml): the gate, in the regions `two-cuts`, `build`, `dotnet-side`, `node-side`, `em-dashes`, `verdict` and `shed-job`.
- [`api/TheYard.Tests/GateWorkflowTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/GateWorkflowTests.cs): the workflow held to the checks, the reports and the verdict.
- [`scripts/test_results.mjs`](https://github.com/SteveStout/TheYard/blob/main/scripts/test_results.mjs): the results file, written from the run's reports.
- [ADR: The five-minute gate](https://theyard.stevenstout.biz/?doc=adr-five-minute-gate): the gate itself, with an addendum pointing here.
