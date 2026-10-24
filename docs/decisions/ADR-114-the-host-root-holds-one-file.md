# ADR: The host root holds one file

Status: accepted, 2026-10-23, at 1.0.4.38. The root of `api/TheYard.Api` holds `Program.cs` and nothing else. Every other file is one slice's code under `Features/<Slice>/` or one concern's code under `Shared/<Concern>/`, and neither `Features/` nor `Shared/` holds a loose file. `HostLayoutTests` fails the build when a file sits where this record says none may.

## Reading list

1. [ADR: The onion cut into vertical slices](https://theyard.stevenstout.biz/?doc=adr-the-onion-cut-into-vertical-slices): the slices this record finishes. Its 1.0.4.24 addendum is where the root stopped shrinking.
2. [ADR: The rules a change has to pass](https://theyard.stevenstout.biz/?doc=adr-rules): the table this record adds one row to.
3. Jimmy Bogard, [Vertical Slice Architecture](https://www.jimmybogard.com/vertical-slice-architecture/): organise by feature, not by the kind of file, which is the line a root of seventy-eight files broke.

## In plain words

Domain-driven design and vertical slices answer two different questions, and this codebase uses both. Domain-driven design decides where the lines go: accounts, bids, vehicles and the Admin tab are different parts of the business, so they are different folders, and the rings keep business rules away from the database and the network. Vertical slices decide what goes inside each line: everything one use case needs, side by side, so a change to bidding opens one folder.

What the slices missed was the root. ADR-107 moved a root file into a slice only when one slice alone used it, and a file two slices share had nowhere to go, so seventy-eight files stayed at the root in alphabetical order, with nothing between `Activity.cs` and `AzureMetrics.cs` but the alphabet. `Shared/` had the same problem one level down: twenty-seven registration and settings files loose beside two folders.

From this record every file has one home. If one slice uses it, it lives in that slice. If more than one place uses it, it lives under `Shared/`, in the folder named for the concern it serves. A newcomer opening the project sees two folders and one file, and the folder names under `Shared/` are the list of things every slice may lean on.

## Context

Measured at 4b460b48 (1.0.4.36): 78 `.cs` files at the root of `api/TheYard.Api` (`Program.cs` and 77 others), 27 loose under `Shared/` beside `Shared/Hosted` and `Shared/Sites`, and a 23-file `Cloud/` folder beside `Features/`. `SliceTests` rule 1 held the root to "no type with one user", a floor the root reached on 10/21 and could not go below, and `SliceTests.StaysShared` named eleven cloud card readers that stayed in `Cloud/` by decision.

The measurement that placed every file reads who uses each file's types: a slice's files count as that slice, and shared code (a registration, the request pipeline, another shared type) counts as shared. A file only one slice uses and no shared code touches moves into that slice. A file used by more than one slice, or by one slice and the shared code that builds the host, is shared. The cloud card readers and the card frame were used by `Features/Admin/Cloud` alone once the folder moved together, so they went into that slice, `NetlifyEdge` and `AzureMetrics` with them. `EdgeKeeps` stayed shared because the request pipeline reads what the edge may keep and writes the header the edge card reads back. The first placement left `NetlifyEdge` shared on the strength of that header, and `AzureMetrics` because `NetlifyEdge` read it; the header's constant was compiled into the pipeline, so the measurement saw one user, the header moved to `EdgeKeeps`, where both of its users read it, and the second check then found `AzureMetrics` with one user too.

## Decision

1. The root of `api/TheYard.Api` holds `Program.cs` alone.
2. `Features/` holds folders alone. Each is one slice named for its domain, and a slice may hold sub-slices (`Admin/Cloud`, `Admin/Plan`, `Admin/Operations`).
3. `Shared/` holds folders alone. Each is one concern, named in the table below, and a concern exists because more than one place uses what it holds.
4. A file's home is decided by measured use, read from the compiled host the way rule 1 reads it: one using slice and no shared user, that slice; anything more, the concern it serves. Name and alphabet decide nothing.
5. The root `Cloud/` folder moves whole into `Features/Admin/Cloud`, with the card frame (`CloudCards`, `CloudThresholds`, `ResourceView`), the recorder and `NightlyResources`.
6. Namespaces follow folders. `Shared/<Concern>/` declares `TheYard.Api.Shared.<Concern>`, subfolders included, and the host project imports every concern once in `TheYard.Api.csproj`, so a slice names a shared type the way it did before the move. No slice is imported there.
7. A concern's name is never the name of a type or of a namespace the code already uses. `Host`, `Azure` and `Plan` would have hidden `Microsoft.Extensions.Hosting.Host`, the Azure SDK's root namespace and a nested `Plan` record, so those concerns are `Instance`, `Resources` and `PlanReadings`. `Composition` is the name of a folder an earlier record retired, so the registrations are `Wiring`.

The concerns under `Shared/`:

| Concern | What it holds | Who uses it |
|---|---|---|
| `Shared/Activity/` | `Activity`, `ActivityCollector`, `ActivityReportCache`, `ActivityWindows`, `Arrival`, `Hits`, `RequestRecorder`, `RequestTimings`, `TrafficMinutes` | written by the pipeline, read by Admin and the blog |
| `Shared/Auth/` | `AdminKey`, `IdentityTokens`, `Tokens` | Accounts issue, Admin, Blog and Docs check |
| `Shared/CostReadings/` | `CostReader`, `CostRecorder`, `CostReport`, `CostWire`, `Costs`, `PlanCost` | the cost cards and the first screen's pulse |
| `Shared/Documentation/` | `DocumentationCatalog` | the docs routes and the Admin page sweep |
| `Shared/Edge/` | `EdgeKeeps`: what the edge may keep and the header each web app names itself in | the request pipeline writes it, the edge card reads it |
| `Shared/Grades/` | `CloudTones` | the cloud, plan and operations cards |
| `Shared/Hosted/` | the hosted services that span slices | the host's start and stop |
| `Shared/Instance/` | `Clocks`, `HostIdentity`, `HostStart`, `HttpCurrentRequest`, `KeepWarm`, `KeepWarmState`, `PageStatus`, `Peer`, `Pulse`, `ResumeDoor`, `SelfAddress`, `SelfTestCooldown`, `ServedAddresses`, `SiteHosts` | the running process, every slice |
| `Shared/MachineReadings/` | `HourlyReadings`, `MachineRecorder`, `MachineSize`, `Machines` | the machines card and the plan card |
| `Shared/Observability/` | `AdminObservability`, `CollectorLoggerProvider`, `ErrorRings`, `KeptRingWriterReader`, `LogRingBuffer`, `Logs`, `Observability`, `ProblemHandler`, `RingSizes`, `StoreLoggers`, `Telemetry` and its two parts | every request and every error |
| `Shared/PlanReadings/` | `PlanDeploys`, `PlanHistoryReader`, `PlanReader`, `PlanRecorder`, `PlanRows`, `PlanShapes`, `PlanView` | the plan card and the cloud cards |
| `Shared/Proof/` | `Proof`, `ProofClients`, `ProofResult`, `ProofRunner` and its two parts | the proof routes and the Cosmos DB card |
| `Shared/Resources/` | `AzureIds`, `AzureResources` | the resource names every Azure card and the plan read |
| `Shared/Sites/` | `IHostedSite`, `HostedSiteMount`, The Shed | The Shed mounts through it |
| `Shared/Stores/` | `Backend`, `CatalogueReads`, `Catalogues`, `DesignTime`, `PreparedStores`, `RelationalContexts`, `StorePrepare`, `StoreSecondChance`, `Stores` | both stores, every slice reads one |
| `Shared/Usage/` | `AnthropicClaudeClient` | the one client that asks Claude, registered by the usage wiring |
| `Shared/Wire/` | `ApiDocument`, `Replies` (kept whole by ADR-076), `VehicleWire` | the public document names every slice's replies |
| `Shared/Wiring/` | `IModule`, `Modules`, every `*Registration`, `RequestPipeline`, `HostPaths`, `AdminSettings`, `SiteSettings`, `StoreChoice` | builds the host every slice runs in |

## What holds it

`HostLayoutTests`, new:

- the root of `api/TheYard.Api` holds exactly one `.cs` file and it is `Program.cs`;
- `Features/` and `Shared/` hold no `.cs` file directly;
- no folder of code sits at the root beside `Features/` and `Shared/`;
- every folder under `Shared/` is a row in this record's table and every row is a folder (the test reads the table, so a new concern is a record change first);
- a file under `Shared/<Concern>/` declares `TheYard.Api.Shared.<Concern>`;
- nothing sits in the host's root namespace;
- a shared type that exactly one slice uses fails with the slice named, read from the compiled host by `SliceTests.LonelySharedTypes`, which rule 1 reads too.

`SliceTests.StaysShared` shrinks from seventeen names to seven: the five replies ADR-076 keeps in `Replies.cs`, and `StoreMetrics` and `Routes`, which share a documented region with what the shared registration builds. The eleven cloud readers and the recorder left the list because they left the root. `CompositionRootTests` holds every file under `Shared/Wiring/` to static classes and records, and every other shared file that registers or maps to the same, which is the rule it held over the loose `Shared/` files before.

## What was considered

Leave the root as it is and call it the shared part. Rejected: seventy-eight files in alphabetical order is the layout ADR-107 was written to end, moved up one level.

One `Shared/` folder with no concern folders inside it. Rejected: it moves the same files one folder down and tells a newcomer nothing.

A `using` line in every file that names a moved type, where the compiler asks for one. Rejected for this move: it touches about two hundred files for no change in meaning and hides the renames in the diff. One import per concern in the project file says the same thing once, and a slice is still never imported.

Cutting the proof and the cost cards out of Admin as their own slices in this version. Moved to the version that makes the Admin reads queries (ADR-115's ladder): the measurement put both in two slices' use (the Cosmos DB card reads the proof, the first screen's pulse reads the costs), so their code is shared and only their routes would move, and those routes get their handlers then.

## Consequences

Every file moved once, so the commit is large and the diff is renames: the moves and the namespace lines are one commit, and the imports, the paths and the tests are the next. Live fences and records that named a moved file by path were repointed; prose was left as written. A type's full name changed for every moved type, so a test that names one by string reads the new name.

## Where it sits

Only the host. The rings below it keep their folders as ADR-107 cut them until the version that slices them (the inner rings' addendum to ADR-107). Single responsibility: ADR-107 says what a slice is, this record says where everything that is not a slice lives. What would change it: a third hosted site with its own slices, which would ask whether `Features/` gets a folder per site.

## Files

- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs) - the one file at the root
- [`api/TheYard.Api/Features/`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Api/Features) - the slices
- [`api/TheYard.Api/Shared/`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Api/Shared) - the concerns
- [`api/TheYard.Api/TheYard.Api.csproj`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/TheYard.Api.csproj) - one import per concern
- [`api/TheYard.Tests/SliceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/SliceTests.cs) - rule 1 and `StaysShared`, which this record narrows
- [`api/TheYard.Tests/HostLayoutTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/HostLayoutTests.cs) - holds this record (new)
- [`api/TheYard.Tests/CompositionRootTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/CompositionRootTests.cs) - the static rule over `Shared/Wiring/`
- [`docs/images/vertical-slices.mjs`](https://github.com/SteveStout/TheYard/blob/main/docs/images/vertical-slices.mjs) - the drawing, redrawn with the concerns
