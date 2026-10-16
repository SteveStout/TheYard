# ADR: The editor is set up by the repository

Status: accepted, 2026-10-13 (1.0.3.121). TheYard and The Shed each carry a `.vscode` folder: the recommended extensions, the workspace settings, every command as a task, and F5. A page under App Architecture, Working in VS Code, walks a new developer through it with screenshots, and a test holds the configuration to the code it names.

## In plain words

Until this record, a developer who cloned this repository got the code and the README and had to set the editor up alone: find the solution file, work out the ports, write a launch configuration, learn which npm script is which. Now the repository carries that setup. Open the folder, accept the extensions it offers, press F5, and the API is under the debugger with the page in Chrome beside it. The Shed gets the same, in its own folder, for the demo shape.

What that is worth: a new developer makes a change and watches it under the debugger inside the first half hour, with nobody walking them through it; and the organization can hand this repository to a new hire, a reviewer or a demo laptop and have it run the same way on each.

## Context

Steve, 13 October: a session "that does nothing but make sure my Yard and Shed are very easy to work with from visual studio code and I have complete readmes and step by step with screen shots and verifies everything is easy for a jr dev or demo". A survey of the tree at 1.0.3.119 found: no `.vscode` folder in either project; a README that gave the commands and nothing about the editor; and a Start here page that said what to run and read but not how to debug or where the tests show up. The dry run that morning, a fresh clone on a four-core laptop, ran every README command green (npm ci 32 s, the solution built with warnings as errors in 47 s, 918 xUnit on SQLite in 63 s, Vitest in 19 s, The Shed's 145 and 21 in 24 s, both servers up and the proxy answering inside 20 s), so the commands were right. What was missing was the editor around them.

Two ways to carry an editor setup were weighed. A `.code-workspace` file with both projects as roots puts two solutions in one window, and C# Dev Kit opens one solution at a time, which leaves a new developer choosing between the two at the first prompt. Two `.vscode` folders, one per project, each opening its own solution, put the choice where it already is: which folder you opened. TheYard's folder also carries The Shed's tasks and its launch configuration, so The Shed runs from the Yard's window without opening it, and The Shed's own folder has the same for the demo.

## Decision

**Four files per project, nothing personal in any of them.** `extensions.json` recommends the extensions the gate's checks assume (C#, C# Dev Kit, Prettier, Oxc, Vitest, Playwright Test, EditorConfig, and the Containers and Bicep extensions for what the site runs on). `settings.json` names the default solution, turns on format on save with the same Prettier and the same `.editorconfig` the gate checks against, pins the project's own TypeScript, and keeps the editor's watcher and search off `bin/`, `obj/`, `node_modules/` and the build outputs, for the same reason `vite.config.ts` keeps Vite's watcher off `api/`: dotnet holds locks on `obj/` while it builds. `tasks.json` holds every command the README gives, as a task with its command shown beside it in the picker, and one compound that runs the checks the gate runs in the order it runs them, without the browser. `launch.json` holds F5: the API under the .NET debugger on the port `launchSettings.json` names, the page in Chrome through Vite on the port the browser suite names, a compound of both, and The Shed with the browser opening on its listening line. Colour themes, keybindings and fonts stay in the user's profile, where they belong.

```live path=.vscode/launch.json region=*
```

**The configuration is held to the code.** `WorkspaceTests` reads the three files with comments allowed and resolves every name: a task that runs `npm run x` fails when `package.json` has no `x`, a task that runs `dotnet build p` fails when `p` is not a project, a launch configuration fails when its DLL path does not match a project in the tree and that project's target framework and assembly name, when its port is not the one in that project's `launchSettings.json`, or when the task it runs first is not in `tasks.json`; the Chrome configuration fails when its port is not Vite's. The same test holds the page to the files: every task and every launch configuration is named on Working in VS Code, through the live sample of `tasks.json` the page carries and the prose around it, so a task added to the file shows on the page that explains it.

```live path=api/TheYard.Tests/WorkspaceTests.cs region=launch
```

**The page shows the real files.** `.vscode/` joined the roots a live block may read from, and the runtime image copies the folder beside the other sources the records read, so the extensions file, the launch file and the tasks file on the page are the ones in the build, not a paste. `.dockerignore` had excluded the folder as editor metadata; it no longer does, because it is documentation now.

**What was left out, on purpose.** No `.code-workspace` file (above). No devcontainer: the three installs are one `winget` line and the page says so. No per-project `omnisharp.json` or Dev Kit settings beyond the solution: the defaults are right for this tree. No task for the full gate: the gate is the ship's, runs on the machine that ships, and takes ten minutes; what a developer runs before a commit is the compound without the browser, about two minutes, and the browser suite as its own task.

## Where it sits

Outside the rings: the editor's configuration, beside the code and read by nothing at run time. The one addition to the application is a root on the live-sample whitelist. Single responsibility: each of the four files answers one question (which extensions, which settings, which commands, what F5 does), and the test answers whether they still match the tree. It costs a folder most developers never open, and a test that reads JSON with comments; what would change it is a second editor in common use here, which would get its own folder and its own row in the test.

## Files

- [`.vscode/extensions.json`](https://github.com/SteveStout/TheYard/blob/main/.vscode/extensions.json), [`.vscode/settings.json`](https://github.com/SteveStout/TheYard/blob/main/.vscode/settings.json), [`.vscode/tasks.json`](https://github.com/SteveStout/TheYard/blob/main/.vscode/tasks.json), [`.vscode/launch.json`](https://github.com/SteveStout/TheYard/blob/main/.vscode/launch.json): TheYard's editor configuration.
- [`samples/theshed/.vscode/`](https://github.com/SteveStout/TheYard/tree/main/samples/theshed/.vscode): The Shed's, for the folder opened on its own.
- [`api/TheYard.Tests/WorkspaceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/WorkspaceTests.cs): the configuration held to the tree and to the page, in the regions `tasks`, `launch`, `settings-and-extensions` and `the-page`.
- [`docs/app-architecture/VSCODE.md`](https://github.com/SteveStout/TheYard/blob/main/docs/app-architecture/VSCODE.md): Working in VS Code, the page.
- [`api/TheYard.Api/LiveSamples.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/LiveSamples.cs): `.vscode/` on the whitelist, in the region `whitelist`.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile) and [`.dockerignore`](https://github.com/SteveStout/TheYard/blob/main/.dockerignore): the folder copied into the runtime image.
