# ADR: The modular monolith

Status: accepted, 2026-10-25, at 1.0.4.76. Each of the two processes, the site and the job hunt's Function, is one deployable cut into modules, and the modules are the vertical slices of ADR-107. A slice's files name no other slice in any ring, the host reaches a module only through its `Module.cs` and its Application ports, and the two processes share rings and a short list of linked source files, never a project or a deployable. `ModularMonolithTests` in both test projects fails the build when any of that stops being true, from one body in `tests/rules/ModularMonolithRule.cs`.

## Reading list

1. Simon Brown, [Modular monoliths](https://gotopia.tech/sessions/515/modular-monoliths) (GOTO, his talk of that name): the structure first, the deployment second, and a monolith that is modular can be split later along lines that already exist. Read 2026-10-25.
2. Kamil Grzybek, [Modular Monolith: A Primer](https://www.kamilgrzybek.com/blog/posts/modular-monolith-primer) (2019): modules with their own boundaries and a public surface, one deployable. Read 2026-10-25.
3. Microsoft Learn, [Common web application architectures](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures): the single-project and the layered monolith, and the clean architecture's rings this site already holds. Read 2026-10-25.
4. [ADR: The onion cut into vertical slices](https://theyard.stevenstout.biz/?doc=adr-the-onion-cut-into-vertical-slices): the slices, which are the modules here.
5. [ADR: The host root holds one file](https://theyard.stevenstout.biz/?doc=adr-the-host-root-holds-one-file) and [ADR: A request is a query or a command](https://theyard.stevenstout.biz/?doc=adr-a-request-is-a-query-or-a-command): what a module's folder holds and how its requests are shaped.
6. [ADR: One host per process, the rings under them](https://theyard.stevenstout.biz/?doc=adr-one-host-per-process-the-rings-under-them): the second process.

## In plain words

The site is one program and the job hunt's Function is another. Neither is a set of small services. Each is cut inside into modules: the auction, bids, accounts, the blog, the Admin tab and the rest for the site; the inbox sweep, the leads, the campaign, the tracker merge, the Sheet and the daily card for the Function. A module keeps its own folder in every layer, and it does not reach into another module's code. What two modules both need lives in a shared folder that belongs to neither.

What that is worth: one deploy and one process to watch, at the cost of a small site's budget, and lines along which a module could be lifted out into a service of its own if it ever had to scale alone, because nothing crosses them today.

## Context

Four records already describe the structure, and none names the whole of it. ADR-088 holds the onion and SOLID, ADR-107 cuts the onion into vertical slices, ADR-114 says what the host's root may hold, and ADR-115 says how a request is shaped. ADR-117 gave the Function the same rings. Together they make a modular monolith, which is the name an interviewer, or a new developer, asks for. Until this version it was nowhere in the records, and two of its rules were held by nothing: that each process is one deployable, and that the two processes share nothing but rings and a few named files.

Measured at 1.0.4.75: the site deploys one web app (`TheYard.Api`) beside a console tool it never deploys (`TheYard.Experiment`); the Function deploys one Functions app (`JobHunt.Functions`); no project of either process references a project of the other; the site's tests compile one file of the Function by a link (`CentralClock.cs`) and the Function's Domain compiles one file of the site by a link (`NightlySlots.cs`), the slot table both clocks read. Rule 1 of the slices held in both processes by reflection (SliceTests, both suites), and the Function's slices since 1.0.4.75.

## Decision

1. **One deployable per process.** The site is `api/TheYard.Api`; the Function is `functions/jobhunt`. A second web host, executable or Functions app under either folder fails the build, with `TheYard.Experiment` named as the one tool that is run by hand and never deployed.
2. **The modules are the slices.** A module is the folder of one slice under `Features/` in every ring (ADR-107). The host's Admin module is three sub-slices (Cloud, Plan, Operations), each with its own `Module.cs`; to the inner rings they are one Admin module.
3. **A module is reached through its registration and its ports.** In the site, a module's `Module.cs` registers and maps it and `Program.cs` loops over the modules; nothing outside a slice names a type inside it (SliceTests). A slice's files name no other slice's namespace, checked in the source as well as the compiled code, so a `using` that names another module is red before anything uses it.
4. **What two modules share is a concern, not a module.** It lives under `Shared/<Concern>/` in its ring (ADR-114's table for the host; the same shape in the inner rings and in the Function since 1.0.4.75).
5. **The two processes share rings, not deployables.** No project references a project of the other process. What crosses is a source file compiled into the other by a link, and each one is named in the test: the slot table (`NightlySlots.cs`, site to Function) and the Central clock (`CentralClock.cs`, Function to the site's tests).

## Consequences

- A module could become a service of its own along a line the build already holds. Nothing in this version does that, and nothing is planned to: one App Service plan carries the site and its guests, and one Flex Consumption app carries the Function.
- A new module costs a folder in each ring it needs and, in the site, a `Module.cs`. A new deployable costs a change to this record first.
- The rules hold the structure, not the quality of what is inside a module. That stays the work of the other records and their tests.

## What holds it

```live path=tests/rules/ModularMonolithRule.cs region=modular-monolith-rule
```

- `ModularMonolithTests` (site): one deployable, a slice's files name no other slice in any ring, every host module registers through a `Module.cs`, the processes share rings and two named linked files.
- `ModularMonolithTests` (Function): one deployable, a slice's files name no other slice, the processes share rings and the one named linked file.
- Rule 1 of the slices in both processes by reflection: `SliceTests` (site, ADR-107's addendum of 1.0.4.67) and `SliceTests` (Function, the addendum of 1.0.4.75).

When the Function's source rule first ran, it found one stale `using` in `CampaignReport.cs` that named the Merge slice after its types had moved to the shared tracker concern. The compiled rule could not see it, because nothing used it. It went in the same version.

## Where it sits

A record over the four that describe the structure, not a change to any of them: it names the whole, and holds the two rules nobody held, one deployable per process and the processes sharing nothing but rings and named files. Separation of concerns is the principle, at the scale of a module rather than a class. It cost one rule file shared by both suites and a test class in each. What would change it: a module that has to scale on its own, which would be lifted out along a line this record already holds, with an addendum here first.

## Files

- [`tests/rules/ModularMonolithRule.cs`](https://github.com/SteveStout/TheYard/blob/main/tests/rules/ModularMonolithRule.cs) - the rules, one body for both suites
- [`api/TheYard.Tests/ModularMonolithTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ModularMonolithTests.cs) - the site's half
- [`functions/jobhunt.Tests/ModularMonolithTests.cs`](https://github.com/SteveStout/TheYard/blob/main/functions/jobhunt.Tests/ModularMonolithTests.cs) - the Function's half
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs) - the loop over the modules

## Addendum, 2026-10-25 (1.0.4.82): the Admin module's fourth sub-slice

Decision 2 named the Admin module's three sub-slices, Cloud, Plan and Operations. From 1.0.4.82 there are four: Foreman, with its own `Module.cs` in the host and its own folder in Application (ADR: The Foreman, a place to start). The prompt for it drew a second Functions app with an identity and a Cosmos DB container of its own; it is a module of the site instead, because every reading it makes is one the site already makes, and Decision 1 holds: still one deployable per process. In the inner rings the four are four slices, so the Foreman's Application folder declares its own records for the failures it cites rather than using the incidents card's, and the host's Foreman adapter, inside the one Admin slice, maps one to the other.

## Addendum, 2026-11-09 (1.0.4.166): a third process, the machine lanes

Decision 1 and Decision 5 named two processes. From 1.0.4.166 there are three. It was built on its own branch from 2 November and ran on the box from then; this is the version it reached main at. The site cannot drive a browser and neither can a GitHub runner, and the job hunt's LinkedIn sends and applications need a Chrome with a signed-in session, which exists on one box, WickedAwsome. So the third process runs there: `server/JobHunt.Server`, a .NET console program a systemd user timer starts at 06:00 America/Chicago. It is not a service, it holds no database, and it shares nothing with the site or the Function at run time; it reads the job-hunt share and writes the same two inboxes every other lane writes.

The rule is the same and the test now names three.

1. **One deployable.** The third process is `server/JobHunt.Server`. A second web host, executable or Functions app under `server/` fails the build.
2. **The same rings.** `JobHunt.Server.Domain` holds the rules with no I/O, `JobHunt.Server.Application` holds the use cases and their ports and references Domain only, `JobHunt.Server.Infrastructure` holds the adapters and references both, and the host sits over them. Its modules are `Run`, `Probe` and `Status`; what they share is a concern under `Shared/`, the same shape as the other two (Decision 4).
3. **Rings and linked files, never a project.** No project of any process references a project of another. What crosses is a source file compiled in by a link, and each one is named in the test. Five cross from the Function to the machine lanes, because a second copy of Rule 50 or of the tracker's CSV reader is how the two would drift apart: `TrackerCsvText.cs`, `TrackerRows.cs`, `ApplicationCap.cs`, `CompanyKey.cs` and `FormerEmployers.cs`.

`ModularMonolithRule.Processes` names the three folders, and `ModularMonolithRule.AcrossProcesses` walks every pair of them. `ModularMonolithTests` in `server/JobHunt.Server.Tests` holds one deployable, the slices, every pair of processes and the five linked files. `ci.yml`, `gate.yml` and the machine's `yard-test` run the suite, and `quality.py` reads the folder, so the third process counts in every number the other two count in.

What this does not change: the site and the Function keep their own halves as they stood, and neither knows the third process exists. It is deployed by `server/publish-to-wickedawsome.sh` on the box, never by a workflow, because no runner can reach a machine at home and nothing here should try.

What an independent read of the send path changed before anything shipped. The first build left the pause at 90 to the model, so a short run now reads the sent page first and the program cuts the worklist from that count. It read Rule 25 by exact name and company over rows filtered by the word "applied", which hid sent messages; it now reads the person on any row, and only a row's opening words decide its kind. It let the apply lane drive linkedin.com while LinkedIn was closed; those rows now stay off the list. A run that ends with no answer now closes LinkedIn for the day, because it may have ended on a checkpoint.

What a run of the code changed before the first live morning. The independent read above traced the code by hand; the pass after it ran the binary on a copy of the share and a played-back model. A text saying "I applied" reached the worklist on a req the tracker had rejected, so `ClosedReqs` now holds a text whose application is over or not on file, and a text naming a live process waits for his yes. An application attached the plain resume, against the rule that every application carries the posting's title, so each row now gets its own build from `build_tailored_resume.py` and its tracker row records the headline and hash. A send the result called failed lost its record although `progress.tsv` held it, a send on a row the worklist never held reached no card, a row reported twice wrote two rows, and a send to a different person read as verified; the file now wins, an off-list send closes LinkedIn, a row counts once, and the reported person is held to the row. A lock whose process had gone turned the next run away in silence, and a share that refused the lock's delete crashed the close; the first is now read as a crash and the second no longer stops the close. An unknown flag now stops the program, so a mistyped `--dry-run` can no longer run live.

## Addendum, 2026-11-09 (1.0.4.166): the feed lane writes the morning's queue, and the hot companies go first

Until this version the invites lane sent only what a person had drafted into a queue file, and from 4 November no one had, so the morning's worklist was empty. A fourth lane, the feed, now runs first in the same 06:00 run. It is a model lane like the others, on Opus at high effort. It may read the share's inputs and LinkedIn, and it writes one file in its run folder, `candidates.json`: for each leader it found, the company, the seat read live, a bucket A to E, the reason, one point in common with its source, the pay read, and the drafted note with a `[code]` placeholder. It has no form tool, so it cannot send.

The program does the rest, in code. `FeedQueue` in the Domain holds every candidate to the gates the sending lanes already run (the person gate with Rule 25, the short checks, the closed-req gate), drops anyone held in `linkedin-proof/FEED-HOLD.md`, anyone already in an open queue block and any second row for one person or one unnamed company, and orders what is left by bucket and then by the model's order. The feed proofreads each text with `check_aiisms.py` through the same port, renders the queue in the shape `QueueBlocks` reads back, and writes `linkedin-proof/QUEUE-hotmgr-<day>.md`, never over one already there; that one name a day is the only path in `linkedin-proof/` `WritePolicy` lets the program write. The day's reads are read again after the feed, so the invites lane works the new queue in the same run. A dry run writes the queue into its own run folder only.

`HotRank` makes the order a rule across files and days: a block's `- Hot:` line gives its bucket, a block with none reads E, a D or E block at a company with an application inside 21 days reads C, and the queue's own order breaks a tie. The invites worklist is ranked by it before the cut to the day's room, so a row left over the room is never hotter than a row kept. A block's `- Profile:` line gives the messages lane the address when the tracker holds none.

Two changes to what counts as a send came with it. The toast lasts seconds. It was lost to the progress write on 4 November. An invite now stands on Pending and its note read back, shown in a saved screenshot, and the toast is kept on the row when seen. A screenshot the program could not copy into the run folder is no proof, so that send stands UNVERIFIED.

The feed reads LinkedIn but sends nothing, so another lane open on LinkedIn does not hold it and its own failures close nothing; only a hard stop it met on the page (a checkpoint, a CAPTCHA, a sign-in, a restriction, the weekly limit) closes LinkedIn for the day, as the sending lanes' do. `FeedTests` holds all of it, from the order of the lanes to a whole run on a played-back model.

## Addendum, 2026-11-09 (1.0.4.168): one queue format between the reply lane and the messages lane

The 4 AM reply lane is a model lane that writes `linkedin-queue.md`, and the messages lane's worklist was built by reading that file's prose with patterns (`ReplyQueue`). The lane wrote what it found in sentences, so on 7, 8 and 9 November every morning's messages DONE note said section C "holds text this program could not read as items" and every accept read "no drafted text": the queue named people and pointed at no draft. The file now has a format of its own, format 2, marked on its first line. Under sections A, B, C and E every line is an item, `- **Person** | Company | Count or thread | draft: <path>` or the same with `held: <why>`, or a `Note:` line; section D stays free text. `ReplyQueueTwo` in the Domain reads it; the messages worklist takes B and C items with a draft (the draft must greet the person named, or, with no person named, the one tracker person at the company with that first name), lists a held item with the lane's own reason, names a missing draft file, and names every line outside the grammar. A and E are Steve's (warm replies, and the rejection reads with their feedback asks) and no program sends them. A queue without the marker is read as before. The newest queue is now the newest real reply run of the day at any slot, so the Function's 5 AM retry (ADR-117, the addendum of this version) is read. The reply lane runs the same grammar on its own file before it closes (`check_reply_queue.py` on the share), and both sides read one fixture pair, `Fixtures/reply-queue.md` and `reply-queue.expected.json`, byte for byte the share's copies. `ReplyQueueTwoTests` in `server/JobHunt.Server.Tests` holds the fixture, a line outside the grammar, the marker, and a whole messages run on a format 2 queue from a later slot.

## Addendum, 2026-11-09 (1.0.4.174): the network lane reads LinkedIn for the network log

A fifth lane runs in the 06:00 run, after the messages lane: the network lane, the LinkedIn half of the network log (ADR-124). It is a model lane on Sonnet at low effort with a 30-minute limit. Like the feed it reads and sends nothing: its tools are the reading ones, and `computer` on a live run to scroll the thread list, no form tool. Its doc, `nightly/LINKEDIN-NETWORK-LANE.md` on the share, keeps the rule the messages lane already keeps: Messaging is entered at the new-message page and never at the bare messaging address, and a thread with an unread message is never opened, so nothing Steve has not read is marked read.

The program hands it two bounds in `high-water.txt` in its run folder: the mark, the newest exchange spooled so far, and the floor, how far back the backlog has reached. The model reads the new threads down to the mark less seven days, then the backlog below the floor back to 46 days, and writes `network-threads.tsv`, one row a thread where both sides wrote, and each thread's text under `threads/`. `NetworkThreads` in the Domain holds each row to the Function's LinkedIn spool shape: the same header, a profile on linkedin.com/in/, a thread id safe as a file name, both sides' times with an offset, a first two-way time no later than either. A row with no text is left out. A live run writes the texts and then the TSV into `networklog/linkedin-inbox/`, reads each back, moves the mark forward and the floor back, and writes the floor as done once the model says the backlog reached its end. `WritePolicy` gains exactly those paths: the inbox's TSV files, `text/<thread>.txt` beneath it, and the two bound files. A dry run checks the same and spools nothing.

Another driver LIVE on LinkedIn, or the day's stop file, holds it as it holds a sending lane, because it is a drive; only a hard stop it met closes LinkedIn for the day. `NetworkLaneTests` holds it, from the lane order to a live and a dry run on a played-back model.
