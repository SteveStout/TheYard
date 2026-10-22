# Ahead-of-time compilation, explained

TheYard is a used-vehicle auction platform built in the open: a .NET 10 API, a React front end, and two databases underneath it. Since 1.0.3.94 the API ships compiled ahead of time: the image carries machine code for this app and its libraries, so a new container starts by running code instead of compiling it on the plan's one shared core. The record of that decision, with the reading before, the rolls after, and what was left out, is [ADR: Compiled before it ships](https://theyard.stevenstout.biz/?doc=adr-compiled-before-it-ships). This page is the teaching half: what the runtime does with a program when nobody has compiled it ahead of time, what one publish switch changes, which settings sit beside it and why, how the result is read, and what five deploys said. Each section has an address of its own, so a link can land on it.

## In plain words

A .NET program is not shipped as machine code. The compiler writes an intermediate form, and the runtime turns each method into machine code the first time that method runs. That step is the JIT, the just-in-time compiler. On a developer's laptop it costs nothing anyone notices. On this site, where both sites share one processor, it is work every new container does on the same core that is loading a hundred thousand vehicles and answering the first visitors after a deploy.

ReadyToRun moves that work to build time. The publish step compiles the app and its packages to machine code for the platform the container runs on, and writes it into the image beside the intermediate code. A container then starts by running code that is already compiled, and the runtime compiles only what it chooses to compile again.

What that is worth: a visitor after a deploy meets a container that is running instead of compiling, a developer can read every runtime setting beside the reason it is what it is, and the organization pays nothing for it beyond a larger image and a longer build.

### The words this page uses

- **Intermediate language, IL**: what the C# compiler writes. Portable, and not yet machine code.
- **The JIT**: the just-in-time compiler inside the runtime, which turns a method's IL into machine code the first time the method is called.
- **ReadyToRun, R2R**: compiling IL to machine code at publish time, for one platform, and keeping both forms in the assembly. The runtime uses the machine code and falls back to the IL where it has to.
- **Tiered compilation**: the runtime's habit of compiling a method quickly first, counting how often it runs, and compiling the hot ones again at full optimization.
- **Profile-guided optimization, PGO**: the second compile of a hot method uses what its first runs measured, so the code it writes fits how the method is actually used.
- **Native AOT**: compiling everything ahead of time and shipping no JIT at all. Faster to start, and closed to libraries that build types at run time.
- **Workstation and server garbage collection**: two modes of the memory collector. Server mode keeps a heap per core and spends memory for throughput; workstation mode keeps one and spends less.
- **A roll**: one deploy. App Service pulls the new image, starts the new container on its one instance, and swaps the old one out.

## What the runtime was doing on one core

Read from each container's own startup timings at `/api/admin/metrics` and the roll probe on Steve's machine, on 1.0.3.92 and 1.0.3.93, the last two builds compiled by the runtime (`rr-startup-before-10392.log`, `orderlane-proof-1.0.3.93.log`, `greenlane-probe-10392.log`).

| Before, compiled by the runtime | Azure SQL site | Azure Cosmos DB site |
| --- | --- | --- |
| Ready, from the process starting (1.0.3.92) | 87.7 s | 87.7 s |
| Ready, from the process starting, the next roll (1.0.3.93) | 79.0 s | 76.4 s |
| Methods the runtime compiled itself, about two minutes in (1.0.3.93) | 32,690 | 32,414 |
| Its time compiling, the same moment | 113 s | 119 s |
| Methods compiled, thirteen minutes in | 62,432 | 63,155 |
| Its time compiling, the same moment | 288 s | 269 s |

The time compiling is a total over every thread, and each compile is timed from start to end, so a thread waiting its turn on the one processor counts too. 113 seconds of it inside the first 106 seconds does not mean the processor did nothing else. It does mean compiling was in the way through most of a container's first two minutes, on the same processor that was loading the catalogue and answering the first visitors.

Most of the 80 to 88 seconds to ready are waits on the network: the schema check against Azure SQL, and a hundred thousand vehicles read from the store. Compiling ahead of time does not shorten a wait. What it can remove is the compiling that shares the core with that work, and the reading that says whether it did is the runtime's own count of what it compiled.

## One publish switch

The Dockerfile restores the API for linux-x64 with ReadyToRun asked for, which fetches the ahead-of-time compiler as a package, and publishes with it on. Framework-dependent: the base image already carries the framework compiled this way, so only this app and its packages are compiled here.

```live path=Dockerfile region=api-publish
```

Nothing in the code changes. A developer's build and the tests run exactly as before, because the switch lives in the publish step and nowhere else.

## The settings beside it, written out

Three runtime settings sit beside the switch in the project file. Two of them are the defaults; they are written out so the choice is read in one place rather than inherited silently.

```live path=api/TheYard.Api/TheYard.Api.csproj region=runtime-settings
```

- **Tiered compilation and PGO stay on.** Code compiled ahead of time is what runs first. The methods that turn out hot are compiled again at full optimization with what their first runs measured, so a warmed process gives up nothing to one that compiled everything at run time.
- **Workstation collection, with background collection on.** The web SDK's default is server collection, which keeps a heap per core. On a plan where memory is the scarcest thing it has, and whose one core leaves server collection little to work with, the smaller footprint is the better trade. On one core the runtime already falls back to workstation mode, so today the setting changes nothing; it is written out so that a move to a plan with two cores is a decision rather than a surprise.

The first draft of this spelled a property wrong, and MSBuild ignored it without a warning. That is why the test below reads the runtime configuration the build writes and not the project's text.

## The reading

The Admin metrics report how much of the process's code the runtime compiled itself since it started, and how long that took, beside the collector's mode and the processor count. It is the one reading that says whether code arrived compiled.

```live path=api/TheYard.Api/Features/Admin/Operations/MetricsReport.cs region=runtime-metrics
```

`JitInfo.GetCompiledMethodCount` counts every method the JIT compiled in this process, and `GetCompilationTime` is the time it spent doing so, summed over every thread. Code that arrived compiled ahead of time is not in the count until the runtime chooses to compile it again at a higher tier.

## The tests that hold it

Three tests keep the page true. One reads the live metrics and holds their shape. One reads the Dockerfile and holds the publish switch. One reads the runtime configuration the build wrote, `TheYard.Api.runtimeconfig.json`, and holds each of the three settings, so a property spelled wrong in the project fails the gate instead of running on the SDK's default.

```live path=api/TheYard.Tests/CompilationTests.cs region=compiled-ahead
```

## What was left out, and why

| Left out | Why |
| --- | --- |
| Native AOT | Starts fastest, and ships no JIT. EF Core, the Cosmos DB SDK, and the reflection the JSON options and the OpenAPI document rely on do not support it without a rewrite. |
| Trimming | Removes unused code from the image. Unsafe with the same reflection, and image size is not what this buys. |
| Invariant globalization | Smaller and slightly faster to start, and it changes how dates and numbers are formatted and compared. Nothing measured here asks for it. |
| A composite, self-contained image | Compiling the framework and the app together starts a little faster again, and means carrying the framework in the image. Worth measuring once this one has its after reading. |

## The whole trip, drawn

[![Compiled before it ships: on the left a container compiled by the runtime, every method compiled on the one shared core the first time it is called; on the right a container compiled ahead of time, machine code in the image and the runtime recompiling only the hot methods](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/compiled-ahead.png)](https://theyard.stevenstout.biz/api/docs/diagrams/compiled-ahead)

*A preview. [Open the drawing in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/compiled-ahead) to zoom in and follow it. The left lane is a container as it shipped before 1.0.3.94; the right lane is the same container compiled ahead of time.*

## Before and after, five rolls

Every roll since was read the same way: each container's own startup timings and runtime reading, the probe on Steve's machine for the roll and the first answers, and the plan's own meters at one-minute grain (`orderlane-proof-1.0.3.9N.log`, `rr-startup-after-103NN-20m.log` and `-75m.log`, `greenlane-probe-103NN.log`, `rrlane-planread\plan-103NN.txt`). Each cell is the Azure SQL site and the Azure Cosmos DB site. Two seventy-five-minute reads fell inside the next roll and are not read.

| | 1.0.3.93, runtime | 1.0.3.94 | 1.0.3.95 | 1.0.3.96 | 1.0.3.97 |
| --- | --- | --- | --- | --- | --- |
| Methods the runtime compiled itself at the first read, and when | 32,690 and 32,414 at 106 and 108 s | 13,429 and 12,567 at 123 and 125 s | 14,710 and 13,938 at 94 and 97 s | 12,702 and 12,273 at 176 and 187 s | 15,284 and 16,525 at 244 and 247 s |
| Its time compiling, the same moment | 113 s and 119 s | 108 s and 113 s | 61 s and 62 s | 148 s and 168 s | 165 s and 170 s |
| Methods compiled at the twenty-minute read | 62,432 and 63,155 | 36,209 and 36,396 | 36,727 and 36,556 | 26,401 and 25,852 | 33,842 and 34,506 |
| Methods compiled at the seventy-five-minute read | 66,685 and 67,165 | 39,864 and 39,769 | not read | not read | 39,762 and 40,142 |
| Its time compiling, the same moment | 618 s and 476 s | 287 s and 308 s | not read | not read | 397 s and 420 s |
| The catalogue loaded | 28.3 s and 26.3 s | 45.9 s and 43.6 s | 31.2 s and 40.1 s | 53.2 s and 67.0 s | 76.6 s and 94.9 s |
| Ready, from the process starting | 79.0 s and 76.4 s | 112.4 s and 108.0 s | 70.6 s and 79.9 s | 152.4 s and 161.2 s | 115.4 s and 132.6 s |
| The roll: longest stretch with no good answer | 131 to 157 s | 148 to 185 s | 119 to 156 s | 271 to 314 s | 151 to 227 s |
| The image in the registry | 221.1 MB | 206.2 MB | 206.2 MB | 206.2 MB | 206.2 MB |

**What held, four rolls running.** The runtime compiled 49 to 62 per cent fewer methods at its first read than it did on 1.0.3.93, on every roll, and 40 per cent fewer at seventy-five minutes on the two rolls that have the read. That is the one thing compiling ahead of time can be measured to do, and it did it every time. The image came out 15 MB smaller, because publishing for one platform leaves out the native files every other platform needed.

**What did not hold.** No roll since has started as fast as 1.0.3.93. Ready moved from 76 and 79 seconds to 71 to 161. The catalogue load, a wait on the network that compiling cannot slow, moved from 26 and 28 seconds to 31 to 95. And the time the runtime spent compiling did not fall with the method count: 61 and 62 seconds on 1.0.3.95 against 165 and 170 on 1.0.3.97, for about the same number of methods. With PGO on, a hot method is compiled again with instruments and then once more at full optimization, and code compiled ahead of time goes through the same steps, so the runtime compiles fewer methods and spends about as long on the hot ones.

**What the plan's meters say.** Around every start the plan's processor read 85 to 99 per cent for most of ten minutes, and its memory 88 to 98 per cent. The saturation began before the containers started: four minutes before at 1.0.3.93, and at 1.0.3.97 from three minutes after the image was pushed, nine before the containers started. The sites' own meter, the CPU seconds each reports for itself, counts little of that core: 24 to 75 seconds summed over the three sites in the four minutes from each start, against 168 to 236 core-seconds the plan reports for the same minutes, and the two rolls that counted least are the two whose loads ran longest. Whether App Service pulling and starting the image took the rest, or the containers' own work before their meter reports it, the two meters cannot tell apart at this grain. The core was taken through every start, and the containers' share of it was smallest on the two slowest rolls.

**What this page claims.** Less compiling, measured every time, and no faster start measured. A reading taken on a saturated core says more about the core than about the setting.

## What would settle it

The same catalogue load, compiled ahead of time and not, in Docker on one machine with the plan's limits (`--cpus=1 --memory=1792m`) and nothing else on the core. That holds the share of the core constant and reads the setting alone. The PGO trade waits on the same reading: profile-guided optimization off for the first minutes would trade some of the warmed process's speed for less compiling at start, and the numbers decide it.

## Where it sits

Outside the rings of the onion: the build and the runtime's configuration. No code changes. The Dockerfile says how the image is built, the project says how the process runs, and the tests hold both to the record. What would change it is a library that supports Native AOT, or a reading that says the larger image's pull costs more at a roll than the compiling it saves.

## If you turn it on: the short list

1. Read what the runtime compiles itself before you change anything. `JitInfo.GetCompiledMethodCount` and `GetCompilationTime` are two lines, and without them you have no before.
2. Publish for the platform the container runs on, framework-dependent, with `PublishReadyToRun=true`. Restore for that platform first, so the ahead-of-time compiler is fetched.
3. Leave tiered compilation and PGO on. Compiled-ahead code is the first tier; the hot methods still get their second compile.
4. Write every runtime setting out in the project, and test the runtime configuration the build writes, not the project's text. A property spelled wrong is ignored without a warning.
5. Say no to Native AOT until every library you run says yes. EF Core and reflection-based serializers are the usual reasons it is no.
6. Read the result on more than one roll, and read the machine beside it. A start measured on a saturated core is a reading of the core.
7. When the number you wanted does not move, measure the setting alone, on one machine with the same limits and nothing else running, before you claim anything either way.

## References

- Microsoft Learn, [ReadyToRun compilation](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run): what the publish switch does and what it costs.
- Microsoft Learn, [Runtime configuration options for compilation](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation): tiered compilation, PGO and ReadyToRun as settings.
- Microsoft Learn, [Runtime configuration options for garbage collection](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector): workstation against server, and background collection.
- Microsoft Learn, [Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/): what it needs from every library.
- .NET API, [`JitInfo`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.jitinfo): the compiled-method count and the compilation time this page reads.
