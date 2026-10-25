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
