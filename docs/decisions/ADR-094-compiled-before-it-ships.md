# ADR: Compiled before it ships

Status: accepted, 2026-10-07 (1.0.3.94). The API is published compiled ahead of time for linux-x64 (ReadyToRun), and the runtime's compilation and collection settings are written out in the project. The before reading is below, and the rolls after it are in the addenda.

## In plain words

.NET ships a program as an intermediate form that the runtime turns into machine code the first time each piece of it runs. On a fast machine that costs little. On this site's plan, one processor shared by both sites, it is work every new container does on the same core that is answering visitors, and part of why the first minutes after a deploy were slow. This record turns on ahead-of-time compilation for the API, so the image carries machine code for this app and its libraries, and writes down the related runtime settings and why each is what it is.

What that is worth: a visitor meets a container that starts by running code instead of compiling it, a developer can see every runtime setting and its reason in one place, and the organization pays nothing for it beyond a larger image and a longer build.

## Context

Steve, 7 October, after the request pipeline shipped: "are we compiling everything before we deploy", and then, ruling on the trade: "We don't care about build time or DLL size we worry more about end performance." The image published the API in plain Release mode: intermediate code only, compiled by the runtime on first use.

Before, read from each container's own startup timings at `/api/admin/metrics` (`rr-startup-before-10392.log`, the containers 74 minutes old), the runtime's count and a second start from 1.0.3.93, the first version that reports it (`orderlane-proof-1.0.3.93.log`, `rr-startup-after-10393-20m.log`), and the roll probe (`greenlane-probe-10392.log`):

| Before (1.0.3.92 unless named) | Azure SQL site | Azure Cosmos DB site |
| --- | --- | --- |
| Ready, from the process starting | 87.7 s | 87.7 s |
| The site's own store prepared (of which the schema check) | 35.2 s (30.3 s) | 8.4 s (4.8 s) |
| The catalogue loaded | 36.9 s | 36.9 s |
| The bids loaded | 1.4 s | 0.8 s |
| Methods the runtime compiled itself, and its time compiling, 106 and 108 s after the process started (1.0.3.93) | 32,690 methods, 113 s | 32,414 methods, 119 s |
| The same, 799 and 800 s after the process started (1.0.3.93) | 62,432 methods, 288 s | 63,155 methods, 269 s |
| Ready, from the process starting, the next roll (1.0.3.93), for how much one start differs from the next | 79.0 s | 76.4 s |

The roll of 1.0.3.92 read every two seconds from Steve's machine: the longest stretch with no good answer was 153 to 168 seconds on every address, and the slowest good answer 27 to 30 seconds.

The runtime's time compiling is the total over every thread, and each compile is timed from start to end, so a thread waiting its turn on the one processor counts too. 113 seconds of it inside the first 106 seconds does not mean the processor did nothing else. It does mean compiling was in the way through most of a container's first two minutes, on the same processor that was loading the catalogue and answering the first visitors.

**What this can and cannot move.** Most of those 88 seconds are waits on the network: the schema check against Azure SQL, and a hundred thousand vehicles read from the store. Compiling ahead of time does not shorten a wait. It removes the compiling that shares the one core with that work and with the first visitors, so the reading that should fall furthest is the runtime's own count of what it compiled, and the first answers after the container comes up. The stretch with no answer at all is App Service replacing the container (ADR: The ports learn to wait, the addendum of 1.0.3.91), and this record does not expect to move it.

## Decision

**ReadyToRun, framework-dependent, for linux-x64.** The Dockerfile restores the API for linux-x64 with ReadyToRun asked for, which fetches the ahead-of-time compiler, and publishes with `-r linux-x64 --self-contained false -p:PublishReadyToRun=true`. This app's own assemblies and its packages (EF Core, the Cosmos DB SDK, Identity) are compiled to native code at build time, beside the intermediate code. The framework already ships compiled this way in the aspnet base image.

```live path=Dockerfile region=api-publish
```

**Tiered compilation and profile-guided optimization, on.** The defaults, written out. Code compiled ahead of time is what runs first; the methods that turn out hot are compiled again at full optimization using what their first runs measured, so a warmed process gives up nothing to one that compiled everything at run time.

**Workstation garbage collection, background collection on.** The web SDK's default is server collection, which keeps a heap per core and holds more memory for throughput. On a plan where memory is the scarcest thing (92 per cent used at the median over ten days, ADR: The ports learn to wait, the addendum of 1.0.3.91) and one core leaves server collection little to work with, the smaller footprint is the better trade. On this plan the setting changes nothing today: with one processor the runtime already falls back to workstation collection, and 1.0.3.93 read `"gc": "workstation"` live on both sites while still asking for server collection. It is written out so that a move to a plan with two cores, where server collection would start keeping a heap per core, is a decision rather than a surprise. The first draft of this version spelled the property `ServerGarbageCollector`, which MSBuild ignores without a warning; a publish on Steve's machine still wrote `"System.GC.Server": true`. So `CompilationTests` reads the runtime configuration the build writes, not the project's text, and a misspelled setting fails it.

## What was left out, and why

- **Native AOT.** It compiles everything ahead of time and drops the runtime compiler entirely, and it would start fastest. EF Core, the Cosmos DB SDK, and the reflection that the JSON options and the OpenAPI document rely on do not support it without a rewrite.
- **Trimming.** Removes unused code from the image; unsafe with the same reflection, and image size is not what this record buys.
- **Invariant globalization.** Smaller and slightly faster to start, and it changes how dates and numbers are formatted and compared; nothing measured here asks for it.
- **A composite, self-contained image.** Compiling the framework and the app together starts a little faster again, and means carrying the framework in the image instead of the base image; worth measuring once this one has its after reading.

## Addendum, 2026-10-07 (1.0.3.96): the first roll compiled ahead of time, measured

1.0.3.94 rolled at 22:18 CDT. The same reads as before, from the same places: each container's own startup timings and runtime reading (`orderlane-proof-1.0.3.94.log`, `rr-startup-after-10394-20m.log`), and the probe on Steve's machine for the roll and the first answers (`greenlane-probe-10394.log`). The time to a first answer is the probe's first good answer after the start time the container reports, so the two machines' clocks sit inside it.

| | 1.0.3.93, compiled by the runtime | 1.0.3.94, compiled ahead of time |
| --- | --- | --- |
| Methods the runtime compiled itself, about two minutes in (SQL site, Cosmos DB site) | 32,690 and 32,414, at 106 and 108 s | 13,429 and 12,567, at 123 and 125 s |
| Its time compiling, the same moment | 113 s and 119 s | 108 s and 113 s |
| Methods compiled, twelve to thirteen minutes in | 62,432 and 63,155, at 799 and 800 s | 36,209 and 36,396, at 722 and 724 s |
| Its time compiling, the same moment | 288 s and 269 s | 248 s and 263 s |
| Methods compiled, seventy-five minutes in | 66,685 and 67,165, at 4,105 and 4,106 s | 39,864 and 39,769, at 4,031 and 4,033 s |
| Its time compiling, the same moment | 618 s and 476 s | 287 s and 308 s |
| From the process starting to the first `/healthz` | 21.2 s and 20.2 s | 28.1 s and 27.9 s |
| From the process starting to the first `/api/version` | 31.1 s and 28.4 s | 50.9 s and 46.5 s |
| Ready, from the process starting | 79.0 s and 76.4 s | 112.4 s and 108.0 s |
| The image in the registry | 221.1 MB | 206.2 MB |
| The roll: longest stretch with no answer | 131 to 157 s | 148 to 175 s, one address 185 s |

**What it did.** The runtime compiled 59 to 61 per cent fewer methods in a container's first two minutes, about 42 per cent fewer by the twelfth minute, and 40 per cent fewer over the first seventy-five, where it also spent 35 to 54 per cent less time compiling (`rr-startup-after-10393-75m.log`, `rr-startup-after-10394-75m.log`). The image came out 15 MB smaller, because publishing for one platform leaves out the native files every other platform needed.

**What it did not do, on this roll.** In the first minutes the time spent compiling barely moved, and the start was slower, not faster. Every step was slower, including the ones that only wait on the network: the schema check against Azure SQL took 29.7 s against 24.7 s, and preparing the Cosmos DB store 13 to 15 s against 8. Compiling ahead of time cannot slow a network wait, so the plan was busier during this roll than the one before. One roll is not enough to separate that from the change. The time compiling holding steady has a likelier cause of its own: with profile-guided optimization on, a method that turns out hot is compiled again with instruments, then once more at full optimization, and code compiled ahead of time goes through the same steps. So the runtime compiles fewer methods and spends about as long compiling the hot ones.

**What comes next.** The next rolls are read the same way. If they agree with this one, the setting to measure is profile-guided optimization off for the first minutes, which trades some of the warmed process's speed for less compiling at start; Steve decides that trade on the numbers.

## Addendum, 2026-10-08 (1.0.3.98): four rolls compiled ahead of time, and the plan read beside them

The three rolls after 1.0.3.94 were read the same way (`orderlane-proof-1.0.3.9N.log`, `rr-startup-after-103NN-20m.log` and `-75m.log`, `greenlane-probe-103NN.log`), and for all five rolls since 1.0.3.93 the plan's own meters were read at one-minute grain, the plan's processor and memory beside the CPU seconds each of the three sites reports for itself (`rrlane-planread\plan-103NN.txt`, `site-103NN-*.txt`). Each cell is Azure SQL site and Azure Cosmos DB site. The seventy-five-minute read of 1.0.3.95 timed out on both sites, inside the roll of 1.0.3.97; the one for 1.0.3.96 found 1.0.3.97 already running, so those two cells are not read.

| | 1.0.3.93, runtime | 1.0.3.94 | 1.0.3.95 | 1.0.3.96 | 1.0.3.97 |
| --- | --- | --- | --- | --- | --- |
| Methods the runtime compiled itself at the first read, and when | 32,690 and 32,414 at 106 and 108 s | 13,429 and 12,567 at 123 and 125 s | 14,710 and 13,938 at 94 and 97 s | 12,702 and 12,273 at 176 and 187 s | 15,284 and 16,525 at 244 and 247 s |
| Its time compiling, the same moment | 113 s and 119 s | 108 s and 113 s | 61 s and 62 s | 148 s and 168 s | 165 s and 170 s |
| Methods compiled at the twenty-minute read, and when | 62,432 and 63,155 at 799 and 800 s | 36,209 and 36,396 at 722 and 724 s | 36,727 and 36,556 at 761 and 763 s | 26,401 and 25,852 at 565 and 571 s | 33,842 and 34,506 at 468 and 470 s |
| Its time compiling, the same moment | 288 s and 269 s | 248 s and 263 s | 210 s and 188 s | 380 s and 404 s | 288 s and 287 s |
| Methods compiled at the seventy-five-minute read, and when | 66,685 and 67,165 at 4,105 and 4,106 s | 39,864 and 39,769 at 4,031 and 4,033 s | not read | not read | 39,762 and 40,142 at 3,715 and 3,718 s |
| Its time compiling, the same moment | 618 s and 476 s | 287 s and 308 s | not read | not read | 397 s and 420 s |
| The catalogue loaded | 28.3 s and 26.3 s | 45.9 s and 43.6 s | 31.2 s and 40.1 s | 53.2 s and 67.0 s | 76.6 s and 94.9 s |
| Ready, from the process starting | 79.0 s and 76.4 s | 112.4 s and 108.0 s | 70.6 s and 79.9 s | 152.4 s and 161.2 s | 115.4 s and 132.6 s |
| From the process starting to the first `/healthz` | 21.2 s and 20.2 s | 28.1 s and 27.9 s | 21.9 s and 18.8 s | 36.2 s and 36.8 s | 25.5 s and 26.6 s |
| From the process starting to the first `/api/version` | 31.1 s and 28.4 s | 50.9 s and 46.5 s | 33.4 s and 42.7 s | 65.0 s and 78.3 s | 83.1 s and 100.2 s |
| The roll: longest stretch with no good answer | 131 to 157 s | 148 to 185 s | 119 to 156 s | 271 to 314 s | 151 to 227 s |
| The image in the registry | 221.1 MB | 206.2 MB | 206.2 MB | 206.2 MB | 206.2 MB |

**What held, four rolls running.** The runtime compiled 49 to 62 per cent fewer methods at its first read than it did on 1.0.3.93, on every roll, and 40 per cent fewer at seventy-five minutes on the two rolls that have the read. That is the one thing compiling ahead of time can be measured to do, and it did it every time.

**What did not hold.** No roll since has started as fast as 1.0.3.93. Ready moved from 76 and 79 s to 71 to 161 s. The catalogue load moved from 26 and 28 s to 31 to 95 s, longer on every roll but 1.0.3.95; the first `/api/version` moved with it. The time the runtime spent compiling did not fall with the method count: 61 and 62 s on 1.0.3.95 against 165 and 170 s on 1.0.3.97, for about the same number of methods.

**What the plan read says.** Both sites start within a second of each other and load their catalogues at once on the plan's one core. Around every start the plan's processor read 85 to 99 per cent for most of ten minutes, and its memory 88 to 98 per cent, dipping to 71 to 84 only in the minutes of the swap. The saturation began before the containers started: four minutes before at 1.0.3.93, and at 1.0.3.97 from 06:12 UTC, three minutes after the image was pushed and nine before the containers started, through 06:24 when the read ended. The sites' own meter, the CPU seconds each reports for itself, counts little of that core. Summed over the three sites in the four minutes from each start, it reads 55 s at 1.0.3.93; 75 s at 1.0.3.94; 52 s at 1.0.3.95; 24 s at 1.0.3.96; 24 s at 1.0.3.97; against 168 to 236 core-seconds the plan reports for the same minutes; the two rolls that counted least are the two whose loads ran longest. What took the rest, the two meters do not say: whether it is App Service pulling and starting the image, or the containers' own work before their meter reports it, the plan's figure and the sites' figures cannot be told apart at this grain. The three rolls of 1.0.3.95 to 1.0.3.97 were pushed 30 and 36 minutes apart, each into the tail of the one before. The cause of the longer loads is not resolved past this. The core was taken through every start, and the containers' share of it was smallest on the two slowest rolls. What held it is not named by the meters.

**What would settle it.** The same catalogue load, compiled ahead of time and not, in Docker on Steve's machine with the plan's limits (`--cpus=1 --memory=1792m`) and nothing else on the core, which holds the share constant and reads the setting alone. Until that reading exists this record claims only what the table holds, which is less compiling, with no faster start measured. The profile-guided optimization trade in the addendum above waits on the same reading.

## Where it sits

Outside the rings: the build and the runtime's configuration. No code changes; the one addition the tests hold is that the Dockerfile and the project say what this record says. Single responsibility: the Dockerfile says how the image is built, the project says how the process runs. It costs a larger image and a longer build; what would change it is a library that supports Native AOT, or a reading that says the larger image's pull costs more at a roll than the compiling it saves.

## Files

- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the restore and the publish, in the region `api-publish`.
- [`api/TheYard.Api/TheYard.Api.csproj`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/TheYard.Api.csproj): the compilation and collection settings.
- [`api/TheYard.Tests/CompilationTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/CompilationTests.cs): the live reading, the Dockerfile's publish, and the runtime configuration the build writes, held to this record.
- [`api/TheYard.Api/MetricsReport.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/MetricsReport.cs): the runtime reading, in the region `runtime-metrics`.
