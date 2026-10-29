# ADR: The gate runs where the memory is

Status: accepted, 2026-10-13 (1.0.3.123). The ship's gate runs on a GitHub runner instead of the laptop that ships. The laptop builds the commit and pushes it to a branch named `gate/<version>`; `.github/workflows/gate.yml` runs every check the laptop's gate ran; the laptop reads the run and, only when it is green, fast-forwards main, which deploys as before. From 1.0.4.101 the run promotes itself: see the addendum of that date.

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
- [`scripts/preflight.sh`](https://github.com/SteveStout/TheYard/blob/main/scripts/preflight.sh): the gate's four quick checks, run before a push of `gate/<version>`.
- [`api/TheYard.Tests/GateVerdictTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/GateVerdictTests.cs): the verdict's own script, run over planted results.
- [ADR: The five-minute gate](https://theyard.stevenstout.biz/?doc=adr-five-minute-gate): the gate itself, with an addendum pointing here.

## Addendum, 2026-10-26 (1.0.4.101): the gate promotes its own green run

Until this version the step between a green gate and a roll ran on the laptop: `ship-from-branch.ps1` on the job hunt share watched the run, downloaded `gate-raw`, wrote `data/test-results.json` and the README's figures from it, folded them into the gate's commit, fast-forwarded main, read the roll on both names and every workflow the push started, queued the reads after the roll and deleted the gate branch. A version shipped only while the laptop, or the Linux machine reading its queue, was on and signed in. On 26 October two lanes' versions waited three hours for a queue nobody was reading, and Steve asked that shipping depend on no machine.

From this version the run promotes itself, and the rule for every lane is **a ship is a push of `gate/<version>`**:

- **`promote`** runs after the gate job and The Shed's are both green. It is the only job that may write to the repository, and only to fast-forward main: `scripts/promote.sh` checks the branch names the changelog's top version, that only the fourth number moved (the first three are Steve's, and a version that moves them is shipped from a machine with his yes), that the served resume is the locked one, that no live Cloud card on either name says the site's identity is missing a role (the share's preflight, moved), and that main is an ancestor of the gate's commit. It writes the results file with `scripts/test_results.mjs` and the README's figures with `scripts/readme_results.py` (moved into the repository from the share), refuses if they touched anything but those three files, folds them into the commit, and pushes it to main without force, four tries, each one checking main is still an ancestor. Nothing is ever force-pushed.
- **The roll.** A push made with the workflow's own token starts no other workflow, so the gate calls Deploy and Deploy Render itself (`workflow_call`, beside their push trigger, which a push from a person still uses), each pinned to the promoted commit and in the deploy-production and deploy-render concurrency groups as before. Both deploys now sign in through the `gate` environment, on a push and on a call alike: its federated credential belongs to the same deploy identity and names no branch, so a call from a gate branch signs in as a push to main always did; no credential was added in Azure. A version that changes only the job hunt's files rolls nothing, as Deploy's path filter has always said. The edge branch, which moves on a Deploy run of its own, is started by hand on main after the called Deploy, so it still moves only after the origin is verified.
- **After the roll**, `scripts/admin_after_roll.py` (moved from the share) reads both names for the version, health, the Admin routes and the page sweep, and the job starts CI on main by hand (the one kind of start the token may make) and waits for it green. Then the gate branch is deleted.
- **A version that changes a workflow** cannot be promoted by the token, which GitHub never lets write to `.github/workflows`. The promote job says so and moves nothing; a person runs the same `scripts/promote.sh` against the run's `gate-raw` and pushes main, and the push trigger rolls it.

What the machine that pushes still does, as the laptop's script did before it pushed, because the gate checks these and does not write them: the drawings and the preview card, the stamp strip and Prettier's write (docs/ci-cd/CICD.md lists the commands). What stays with a machine, because it needs Steve's own sign-in: pruning the Azure registry to ten images (the deploy identity cannot delete there; Deploy's own prune step still tries and warns), and the live look and load proof that write their pictures to the share.

`PromoteWorkflowTests` holds the order, the permissions and the no-force rule, and `CiTriggerTests` and `DeployWorkflowTests` still hold the push triggers.

1.0.4.101 changed the workflows itself, so it was the first version promoted by hand under the new rule: its gate run (40424564652) went green in 870 seconds and its promote job refused with the sentence above, and `scripts/promote.sh` was run against that run's `gate-raw` from the Linux machine on 26 October, which wrote 4,060 test runs into the results file and fast-forwarded main to 9f22e420. 1.0.4.102, this line, is the first version that went from a push of its gate branch to the live footer with no machine involved.

## Addendum, 2026-10-26 (1.0.4.105): nothing the site needs waits on a machine

After 1.0.4.101 four things still waited on the laptop or the Linux machine: drawing the version card before a push, deploying the job hunt's Function, saving a blog post's body, and reading Azure Monitor. Steve, 26 October: "I really want these workflows as independent and isolated and robust as possible." From this version each is a workflow of its own, signing in, where it signs in at all, as the deploy identity through the `gate` environment, and each fails on its own run with its own red:

- **The version card is the gate's.** The gate job draws it (`node docs/images/og.mjs`) from the commit's own changelog before any test reads it, and puts it in `gate-raw`; promote folds it into the commit with the results. A session that pushes a gate branch no longer has to draw anything; the other drawings change only with what they draw.
- **The Function's suite is in the gate.** Until this version only CI ran `functions/jobhunt.Tests`, after main had moved, so a version that broke the Function could be promoted; the gate now runs it, the verdict reads it, and the results file lists it as a check.
- **`deploy-function.yml` deploys the job hunt's Function** with the laptop script's steps (publish, zip, config-zip, then the functions, the runtime and the health route read back, 200 or the 204 of a cancelled read). It never sets an app setting, so a deploy cannot turn the campaign on or off. The gate's after-roll starts it as a run of its own when a version touched `functions/`, so a Function that will not deploy never turns a site ship red; it also runs by hand.
- **`blog-post.yml` saves a post's body** from `data/blog/<slug>.md` in the commit: it reads the post from the live API, changes only the body, saves it with the repository's `ADMIN_KEY` secret through a header file, and reads both names back until each serves the file's SHA-256.
- **`azure-read.yml` reads Azure Monitor** by hand, read only, every metric with a start and an end, its raw answers as the run's artifact (the first read is the Cosmos DB throttling split).

What still needs Steve, once: the deploy identity has no role on `func-jobhunt-ss` or delete on the registry yet, so `deploy-function.yml` stays red until he grants the first, and Deploy's prune keeps warning until he grants the second (J:\mentor\BLOCKERS-FOR-STEVE-2026-10-27.md holds the two commands). The laptop's queue runner has nothing of TheYard's left to run.

`MachineFreeWorkflowsTests` holds each workflow to that shape, and `GateWorkflowTests` holds the Function's suite to the verdict.

## Addendum, 2026-10-27 (1.0.4.107): the gate reads every commit it ships, and nothing rolls a commit main has left

The review of 27 October read every run of the night of 26 October and found the promote path sound but held by habit in five places. Every gate branch that night was one commit on main and every hand promotion was run on the right commit, but nothing made either so; a re-run of an older run could roll the site, or the Function, back to it; and `deploy-function.yml`, which had never yet run green, could pass a deploy it had not checked. From this version:

- **The two cuts read the whole branch.** Whether `dotnet format` runs and whether the passes on Cosmos DB run are decided from the diff between `git merge-base origin/main HEAD` and HEAD, with the gate's checkout holding the full history, so a code change in the first of two commits is never carried forward on a changelog-only second.
- **promote.sh checks the commit before it writes anything.** It takes the gate run's id (the gate passes its own; a person promoting by hand names the run they downloaded) and refuses unless that run tested HEAD, its gate and shed jobs are both green, and HEAD is exactly one commit on main. The version must go up under `sort -V` by exactly one in its fourth number.
- **Every promoted version rolls.** The flag that skipped the roll for a version of the job hunt's files alone was always true, because the results file and the README are folded into every commit and the site serves the results file; it is gone, and Deploy and Deploy Render are called for every promoted version.
- **A re-run never rolls back.** Deploy and Deploy Render refuse a commit that is not main's head. The after-roll dispatches the Function only while its commit is still main's head, and `deploy-function.yml` refuses a commit that is not on main.
- **The calls hand over what they use.** Deploy declares its three secrets (`YARD_AUTH_SIGNING_KEY`, `ADMIN_KEY`, `NETLIFY_TOKEN`) and the gate passes them by name; Deploy Render is passed none, where both had every repository secret.
- **After-roll reads its own CI run.** It records the time before it dispatches CI and reads only a run created after it, and it has seventy-five minutes for its reads, CI's start and CI itself.
- **The Function's deploy proves itself.** Its steps run in bash with pipefail; an empty function list before the deploy is red and the list after it is tried five times; the records-run wait is read just before the zip deploy and also covers five to the hour before; the health route names the commit it was published from (`MergerWatch.Commit`, stamped by `dotnet publish -p:SourceRevisionId`), and the deploy is done only on a 200 that names the deployed commit, within five minutes. A 204, a read the host cancelled, never counts as healthy, and a run that saw only 204s is red. It has forty-five minutes.

What this version does not do: gate.yml stays at 377 lines; moving promote and after-roll into a reusable workflow is left for a version of its own. The `gate` environment still has no branch policy and main no ruleset; those are settings, and Steve's (J:\mentor\FIX1027-DONE-2026-10-27.md).

`PromoteScriptTests` runs promote.sh against a bare repository and holds each refusal to leaving main where it was; `PromoteWorkflowTests` and `MachineFreeWorkflowsTests` hold the workflows to these lines, and `RunLineTests` holds the commit the health route names.

## Addendum, 2026-10-27 (1.0.4.108): a card read is JSON or refused, a read is kept a week, and a blog save cannot drop its slides

Three of the review of 27 October's smaller findings on the workflows of 1.0.4.105, from this version:

- **promote reads the Cloud cards as JSON.** It asks for `application/json`, and a card that answers with anything but a JSON object (a page in front of the API, a sign-in) refuses the promotion, where it read as a card with no missing role. A card that does not answer at all is still said and not judged.
- **`azure-read.yml` keeps its raw answers seven days** on this public repository, and runs one read at a time.
- **`blog-post.yml` will not save a post whose live read lost its slides or its tags**, since the save writes back what the read gave and every post has both; and the read-back after the save tries a failed read again rather than failing a save that landed.

`PromoteScriptTests` holds the card refusal by running promote.sh against a card that answers a page; `MachineFreeWorkflowsTests` holds the other two.

## Addendum, 2026-10-29 (1.0.4.119): a gate that passed never sends a failure email

Steve, 29 October: "I am getting a lot of these emails, any way for my deploy script to catch these errors?"
The last thirty Gate runs, 26 to 29 October, were read: eleven failed, and **seven of the eleven were a green
gate refusing to promote itself** (runs 40903343066, 40889845665, 40884853607, 40522513222, 40518359561,
40437977063, 40424564652). Each of those versions changed a file under `.github/workflows`, which the run's own
token may never push, so the promote job said so with `::error::` and exited non-zero. Every check had passed.
GitHub sends a failure email for a failed run, so each of those seven told the owner his code was broken when
nothing was. The other four were real reds in the gate job's verdict, and each of those emails carried the tails
of the logs of checks that had passed beside the one that had not, so "All matched files use Prettier code style!"
arrived as an error annotation. An email that is wrong seven times in eleven is an email nobody opens, and then
the four that matter are not read either.

Three things change from this version, and a fourth moves the catching earlier:

- **A workflow version promotes itself when it can.** The secret `PROMOTE_TOKEN` is a fine-grained personal
  token scoped to this repository with Contents and Workflows write. The promote job checks out with
  `secrets.PROMOTE_TOKEN || github.token`, so where the secret exists the push `scripts/promote.sh` makes at the
  end carries the workflow, and where it does not the checkout is the run's own token as before. The script is
  told only whether the secret existed (`HAS_PROMOTE_TOKEN`, the result of `secrets.PROMOTE_TOKEN != ''`); it
  never reads, prints or passes the value, and the only two mentions of the secret in the workflow are the
  checkout's token and that one test. Nothing is ever force-pushed; the four tries and the ancestor check in
  front of each are unchanged.
- **A workflow version with no such secret ends GREEN.** The promote job prints
  `::notice::this version changes a workflow: promote it by hand with scripts/promote.sh`, writes
  `promoted=false` to its outputs and exits 0, and the deploy, deploy-render and after-roll jobs skip on that
  output. A person then promotes the run by hand exactly as before. The sentence is the same sentence; what
  changed is that it is a notice on a green run instead of an error on a red one, because a run that found
  nothing wrong must not say it did.
- **The push is the roll when a personal token made it.** A push made with the run's own token starts no
  workflow, which is why the gate calls Deploy and Deploy Render itself. A push made with a personal token
  starts them, and CI, and through Deploy the edge's move. So when promote pushed with `PROMOTE_TOKEN` it
  reports `by_push=true`, the two deploy jobs skip rather than put a second run in the deploy-production and
  deploy-render concurrency groups for one commit, the edge is not started by hand because `edge-branch.yml` is
  already watching the push's own Deploy run, and after-roll waits for the push's two runs by their head commit
  before it reads the version on either name and reads the push's CI run instead of dispatching another. The
  Function's deploy is unchanged on both paths: `deploy-function.yml` has only a `workflow_dispatch` trigger, so
  no push ever starts it. Every promoted version still rolls exactly once, by one path or the other.
- **The verdict prints only what failed.** Each log belongs to one check, and its tail reaches the annotations
  only when that check is in `$red`. A red run on the quality ratchet now names `quality.txt` and nothing else.
  The build's warnings, which were red with no evidence printed at all, get their `: warning` lines too.
- **`scripts/preflight.sh` catches what it can before the push.** `dotnet format api/TheYard.slnx
  --verify-no-changes`, `npm run format:check`, `npm run lint` and `npx tsc -b`, the gate's own four commands,
  under three minutes and inside 8 GB, each named with the first twenty lines of its output when it is red, and
  then one line saying whether this version changes `.github/workflows` and whether `PROMOTE_TOKEN` will carry
  it, read from the repository's secret NAMES and never a value. It does not run the browser suite or the two
  passes on Cosmos DB: those need the 16 GB runner, which is the whole reason the gate moved here. A check this
  machine cannot run says UNKNOWN and is never assumed green. The ship rule in CLAUDE.md and
  docs/ci-cd/CICD.md is now: run it, and a red preflight is never pushed. `dotnet format` is in it because the
  before-the-push list never named that command and the first gate of 1.0.4.117 died on it.

What this version does not do. It does not create the secret: a credential is the owner's to make, and until he
does, every workflow version takes the green notice path, which is the path this version itself took. So the
token path ships held by tests and unexercised live, and the first version to change a workflow after the secret
exists is the one that proves it. It does not touch Azure, any role or any setting.

`PromoteScriptTests` runs `promote.sh` against a bare repository on each of the three paths and holds all three
to ending green, to whether main moved and to the outputs each writes; `GateVerdictTests` lifts the verdict's own
script out of the workflow and runs it over a planted `gate-raw` where one check is red among green ones, and
holds the annotations to naming only the red one's log; `PromoteWorkflowTests` holds the token, the notice, the
skips and the preflight, and that the secret is never read, printed or put on a command line.

`GateVerdictTests` skips where **bash is absent**, not where Windows is, which is the older guard in
`PromoteScriptTests`. The first push of this version proved why: two of its cases asserted the annotation with
the log's line trimmed, the verdict emits it as the log holds it, and the gate's runner was the only machine
that could see the difference, so the mistake cost a whole gate run instead of a minute. The laptop that ships
has Git Bash, and a case only the runner can run is a case the push finds out about the expensive way.
