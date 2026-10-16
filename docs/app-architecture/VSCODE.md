# Working in VS Code

This page takes a developer from a clean machine to a breakpoint in the API, a Chrome window on the page and three green test suites, in VS Code, with screenshots of the editor as it goes. It covers TheYard and The Shed, because both live in this repository and both are set up the same way. Everything here was run again from a fresh clone of the shipped commit on 13 October 2026, and the times beside each step are from that run.

## In plain words

Open the folder, say yes to the extensions it recommends, press F5. That is the whole setup, because the repository carries its own editor configuration: what to build, what to run, which ports, which tests, and which extensions, all in a `.vscode` folder beside the code. A new developer gets the same editor as everyone else without copying settings from anyone.

What that is worth: a developer makes a first change and watches it under the debugger inside the first half hour, with no walkthrough; and the organization can hand this repository to a new hire, a reviewer or a demo laptop and have it run the same way every time.

## Before you start

Four installs, once per machine. On Windows, one line in a terminal does the first two:

```
winget install OpenJS.NodeJS.LTS Microsoft.DotNet.SDK.10
```

| What | Why | How to check |
| --- | --- | --- |
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | The API, The Shed and the xUnit tests. | `dotnet --version` prints 10.x |
| [Node 24](https://nodejs.org) | The React page, Vite, Vitest, Playwright and The Shed's TypeScript build. | `node -v` prints v24.x |
| [Google Chrome](https://www.google.com/chrome/) | The browser suite runs in the real Chrome (`channel: 'chrome'` in `playwright.config.ts`), and F5 opens the page in it. | Chrome opens |
| [VS Code](https://code.visualstudio.com) | The editor this page is about. | `code --version` |

No database to install. With no connection string the API writes to a SQLite file in your temp folder and the catalogue is read from `data/`, so the first run serves 100,000 vehicles on its own. The Shed needs nothing but the SDK: its page is TypeScript compiled to `wwwroot/js`, and the output is committed.

## Step 1: clone and open

```
git clone https://github.com/SteveStout/TheYard.git
cd TheYard
npm ci
code .
```

`npm ci` installs the front end's packages exactly as `package-lock.json` has them (19 seconds on a four-core laptop). `code .` opens the folder in VS Code. The first time it opens, VS Code reads `.vscode/extensions.json` and offers the extensions this repository is built with:

![VS Code on the TheYard folder: the Explorer on the left, a file from render/ in the editor, the terminal below, and the notice at the bottom right offering the recommended extensions](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/vscode-open-folder.jpg)

## Step 2: install the recommended extensions

Press **Install All** on that notice, or open the Extensions view (Ctrl+Shift+X) and type `@recommended`. The list is the one in the file below, each with its reason beside it:

```live path=.vscode/extensions.json region=*
```

![The Extensions view filtered to @recommended: the nine workspace recommendations, EditorConfig, Playwright Test, Bicep, Vitest and Oxc in view with an Install button on each, and the Oxc page open beside them](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/vscode-extensions.jpg)

C# Dev Kit opens `api/TheYard.slnx` on its own, because `.vscode/settings.json` names it as the default solution. Give it a minute the first time: it restores the ten projects and the Testing panel fills with the xUnit tests.

## Step 3: run it

**Terminal > Run Task** (or Ctrl+Shift+P, then "Run Task") lists every command in `.vscode/tasks.json`. Pick **Yard: start (API + web)**.

![The Run Task picker: Select the task to run, with every task listed and the command it runs under its name, The Shed's five first and then the Yard's](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/vscode-run-task.jpg)

The task is `npm start`: one terminal, the API in blue on `http://localhost:5210` and Vite in green on `http://localhost:5173`, and Chrome opens on the page when Vite is ready. Vite proxies `/api` to the API, so the page and the API are one address in the browser. From the fresh clone Vite was ready in 3 seconds and the API, which builds first, answered in 40.

![The task's terminal: Executing task: npm start, then the api lines as the .NET host starts and asks the store whether the vehicles and photos tables exist, with the Yard: start terminal selected in the list on the right](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/vscode-start-terminal.jpg)

To stop it, press the trash icon on that terminal, or Ctrl+C in it. The other run tasks start one half on its own (**Yard: API only**, **Yard: web only (Vite)**), which is what the debugger uses.

## Step 4: F5, a breakpoint on each side

Open **Run and Debug** (Ctrl+Shift+D). The configurations come from `.vscode/launch.json`; pick **Yard: API + web (F5)** from the list at the top (it is the last of the four; VS Code starts on the first, **Yard: API**, and remembers your choice after that). It is a compound that starts the API under the .NET debugger and then Chrome, attached, on the page. Press F5.

```live path=.vscode/launch.json region=*
```

Put a breakpoint on the first line of `List` in `api/TheYard.Api/Endpoints/VehicleEndpoints.cs` (click in the gutter left of the line number), then press Load more on the page. The debugger stops in the API with the request's query in the Variables pane; F5 lets it go. A breakpoint in a `.tsx` file under `src/` stops Chrome the same way, through Vite's source maps.

![Run and Debug with Yard: API + web (F5) running: the API stopped on line 64 of HealthEndpoints.cs, Version, with build in Locals, the call stack holding both sessions with the Chrome one running, and the debug toolbar at the top](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/vscode-debug-breakpoint.jpg)

The compound builds the API first (the task **Yard: build API**, also Ctrl+Shift+B), waits for Vite to print its address, and stops both when either stops. The ports are the ones in `api/TheYard.Api/Properties/launchSettings.json` and `vite.config.ts`, written out in `launch.json` so the page and the proxy always meet; `WorkspaceTests` holds the three files to each other.

## Step 5: the tests, in the Testing panel

Open **Testing** (the flask icon). Three trees: xUnit from C# Dev Kit, Vitest from its extension, and Playwright from its; each tree appears once its extension is installed, and the xUnit one after C# Dev Kit has loaded the solution and found the tests (about a minute the first time). Each test runs or debugs from the play button beside it, and a red one opens on the failing line.

![The Testing panel with only C# Dev Kit installed: TheYard.Tests (net10.0) expanded to its test classes, ActivityEndpointTests down to CostTests in view, with HealthEndpoints.cs in the editor](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/vscode-testing.jpg)

The same suites as tasks, each the README's own command, measured from the fresh clone:

| Task | Command | What it runs | Fresh clone |
| --- | --- | --- | --- |
| Yard: test (Vitest) | `npm test` | The presentation logic, no browser. | 9 s |
| Yard: test (xUnit on SQLite) | `npm run test:api` | Every xUnit test but the seven that need the live Cosmos DB account: 924 passed. | 42 s |
| Yard: test (browser, Playwright) | `npm run test:e2e` | The real stack in Chrome. It starts both servers itself, so stop **Yard: start** first. | 9 minutes, 156 passed |
| Yard: lint | `npm run lint` | oxlint, warnings denied. | 1.5 s |
| Yard: format check | `npm run format:check` | Prettier, the way the gate checks it. | 11.5 s |
| Yard: format check (C#) | `npm run format:api:check` | dotnet format with no changes allowed. | 40 s |
| Yard: everything the gate checks (no browser) | the six above, in order | What to run before a commit. | 108 s |

**Both unit suites, one command:** `npm run test:all` runs **Yard: test (xUnit on SQLite)** and then **Yard: test (Vitest)**, each with its output captured, and prints one line per suite:

```
TheYard - all tests
C# tests (xUnit)      943 passed    0 failed   37.3 s
TypeScript tests      419 passed    0 failed   10.6 s
ALL GREEN - 1362 tests passed
```

A failing suite prints everything it printed, then the summary, and exits non-zero. `npm run test:all:full` runs the same two commands with their whole output. The browser suite stays its own command, `npm run test:e2e`, because it needs the app running and starts both servers itself.

The editor formats on save with the same Prettier and the same `.editorconfig` the gate checks against, so a change arrives at the gate already formatted. What the gate itself runs, and why a build warning is red, is in [ADR-068, the five-minute gate](https://theyard.stevenstout.biz/?doc=adr-five-minute-gate).

## Step 6: The Shed

The Shed is the file browser under `samples/theshed`, one .NET project with its page in plain TypeScript. It runs from this window and from its own.

**From this window:** select **Shed: run** in Run and Debug and press F5. It builds, starts on `http://localhost:5120`, and the browser opens on the page when the API says it is listening. The configuration serves plain http, so it tells the C# extension not to ask for an HTTPS development certificate (`checkForDevCert` is false). Its tasks (**Shed: build TypeScript**, **Shed: test (xUnit)**, **Shed: test (node)**) are in the same Run Task list.

**On its own, which is the demo shape:** File > Open Folder on `samples/theshed`. That folder carries its own `.vscode` with the same launch configuration and tasks, and C# Dev Kit opens `TestProject.sln`. F5 does the same thing.

**Every test, one command:** in `samples/theshed`, run

```
npm run test:all
```

It runs the 145 xUnit tests (`dotnet test` builds first), then the TypeScript build, then the 21 node tests, and stops at the first failure. Each step's output is captured, and a green run prints one line per step:

```
The Shed - all tests
C# tests (xUnit)     145 passed   0 failed   19.2 s
TypeScript build     clean                    1.5 s
TypeScript tests      21 passed   0 failed    1.4 s
ALL GREEN - 166 tests passed
```

A failing step prints everything it printed, then the summary, and exits non-zero. `npm run test:all:full` runs the same three commands with their whole output. With The Shed open as its own folder they are the tasks **Shed: test all** and **Shed: test all (full output)** in Run Task. It is the one command to run before a commit.

![The Shed opened as its own folder with Shed: run (F5) running under the debugger: the debug toolbar at the top, HomePath.cs in the editor, and the Debug Console as the host starts on http only](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/vscode-shed-f5.jpg)

F5 browses `sample-home/` inside the repository, so a file uploaded during a demo lands in the working tree. To keep the tree clean, copy `sample-home/` somewhere else and point `FILES__HOME` at the copy, as below.

To browse a folder of your own instead of `sample-home/`, add `"FILES__HOME": "C:\\some\\folder"` to the `env` block of the launch configuration. After any change under `src/`, run **Shed: build TypeScript** and commit `wwwroot/js` with the change: `dotnet run` needs no Node, which is the point of committing the output ([ADR-006 of The Shed, TypeScript organised](https://theshed.stevenstout.biz/?view=docs&doc=adr-006-typescript-organised)).

## A ten-minute demo

For showing the two projects to someone, in order, with nothing to type:

1. Open the TheYard folder. Run and Debug, **Yard: API + web (F5)**. The page opens on Home; open **Inventory**, filter by make, open a vehicle, **Sign in** (create an account; it lives in the local SQLite file), place a bid, and watch the room answer within a few seconds.
2. Set a breakpoint in `VehicleEndpoints.cs`, press Load more, step once, let it go. The API owns the rules; the page asks.
3. Testing panel: run one xUnit test from the play button (and one Vitest test, once the Vitest extension is installed). Point at `OnionTests`: the rings are tests.
4. Open any record from the sidebar on the page (Decision Records): the code in it is read from the build you are running.
5. Stop. Open `samples/theshed` in a new window, F5. Browse, search `*.csv`, upload a file, drop a breakpoint in `FilesController.cs`, download it back.

Every step here is a task or a launch configuration, so the demo is the same on any machine the repository is cloned to.

## VS Code: the files this page added, and why

This page shipped with the editor configuration it describes. Every file, and what it is for:

| File | What it does |
| --- | --- |
| `.vscode/extensions.json` | The recommended extensions, each with its reason as a comment. VS Code offers them when the folder opens. |
| `.vscode/settings.json` | The default solution, format on save with Prettier and dotnet format, LF line endings, the project's own TypeScript, and the watcher and search exclusions for `bin/`, `obj/`, `node_modules/` and the build outputs. Nothing in it is a matter of taste. |
| `.vscode/tasks.json` | Every command the README gives, as a task: run, build, the three suites, the checks, and The Shed's own, each with the command it runs shown beside it in the picker. |
| `.vscode/launch.json` | F5: the API under the debugger, the page in Chrome through Vite, the compound of both, and The Shed with its browser opening when it listens. |
| `samples/theshed/.vscode/` | The same four files for The Shed opened as its own folder: its solution, its tasks, and F5. |
| `docs/app-architecture/VSCODE.md` | This page, served under App Architecture after Start here. |
| `docs/images/vscode-*.jpg` | The seven screenshots on this page, taken in VS Code on the laptop it was rehearsed on. |
| `api/TheYard.Tests/WorkspaceTests.cs` | Holds the configuration to the code: every task runs a script `package.json` has or a project that exists, every launch configuration's program is a project in the tree on the port its `launchSettings.json` names, every task is on this page, and the extensions file names the C# and test extensions. |
| `api/TheYard.Api/LiveSamples.cs` | `.vscode/` added to the roots a live block may read from, so this page shows the real files. |
| `Dockerfile`, `.dockerignore` | The runtime image copies `.vscode/` beside the other sources the records read, and the ignore file stopped excluding it. |
| `README.md`, `samples/theshed/README.md`, `CLAUDE.md` | Each gained the line that points here. |
| `docs/decisions/ADR-097-the-editor-is-set-up-by-the-repository.md` | The record: why the configuration is in the repository, what it holds, and what was left out. |

The tasks, read from the build:

```live path=.vscode/tasks.json region=*
```

## When something is off

| What you see | What it is | What to do |
| --- | --- | --- |
| Vite dies with `EBUSY` | dotnet holds locks on `obj/` while it builds, and a watcher that reads it crashes. `vite.config.ts` excludes `api/` from Vite's watcher and `settings.json` excludes it from the editor's. | Keep both exclusions. |
| The browser suite goes red on the bid and Admin tests | It reuses a server already running on 5210 or 5173 (`reuseExistingServer` in `playwright.config.ts`), and that server lacks the settings the suite gives the servers it starts. | Stop **Yard: start**, then run the suite. |
| `npm ci` prints audit warnings | The audit reads the lock file's development packages; nothing in it ships to the browser or the container. | Read `npm audit`; the gate's own lint and tests are what hold the build. |
| F5 says it cannot find `TheYard.Api.dll` | The build task did not run, or the SDK is not 10. | `dotnet --version`, then Ctrl+Shift+B. |
| The Testing panel shows no xUnit tests | C# Dev Kit is still restoring the solution, or it opened the wrong one. | Wait for the status bar, or Ctrl+Shift+P, ".NET: Open Solution", `api/TheYard.slnx`. |
| The status bar shows **Projects** in red and the Testing panel stays empty | C# Dev Kit's project system did not start; its log (Output, C# Dev Kit) names an `IComponentExportsService` error. Seen once on 13 October, on the first open of the day. | Close VS Code and open the folder again; it loaded on the second open. |
| `dotnet run` in The Shed lands on a certificate warning | Its launch profile listens on `https://localhost:7146` as well as `http://localhost:5120`, and in Development the http address forwards to https. | Run `dotnet dev-certs https --trust` once, or use F5, which is http only. |
| C# formatting differs from the gate | The gate runs `dotnet format` on the solution; the editor formats one file. | Run **Yard: format check (C#)** before committing. |

## Files

- [`.vscode/extensions.json`](https://github.com/SteveStout/TheYard/blob/main/.vscode/extensions.json), [`.vscode/settings.json`](https://github.com/SteveStout/TheYard/blob/main/.vscode/settings.json), [`.vscode/tasks.json`](https://github.com/SteveStout/TheYard/blob/main/.vscode/tasks.json), [`.vscode/launch.json`](https://github.com/SteveStout/TheYard/blob/main/.vscode/launch.json): TheYard's editor configuration.
- [`samples/theshed/.vscode/`](https://github.com/SteveStout/TheYard/tree/main/samples/theshed/.vscode): The Shed's.
- [`api/TheYard.Tests/WorkspaceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/WorkspaceTests.cs): the configuration held to the code and to this page.
- [`api/TheYard.Api/Properties/launchSettings.json`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Properties/launchSettings.json) and [`samples/theshed/Properties/launchSettings.json`](https://github.com/SteveStout/TheYard/blob/main/samples/theshed/Properties/launchSettings.json): the ports.
- [ADR-097, The editor is set up by the repository](https://theyard.stevenstout.biz/?doc=adr-the-editor-is-set-up-by-the-repository): the decision.
