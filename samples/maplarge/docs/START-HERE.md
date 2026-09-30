# Start here

The Shed is a file and folder browser: an ASP.NET Core 10 API over one configurable home directory,
and a single page in TypeScript, with no framework, that browses, searches, uploads, downloads,
makes folders, moves, copies and deletes, with the whole state of the page in the address bar. It
was built for MapLarge's developer test project in September 2026, on their starter, in the working
method of [TheYard](https://theyard.stevenstout.biz).

## Three things a reviewer opens first

- [The Shed, explained](/the-shed-explained.pdf): three pages, the numbers, the quick start, the
  choices and why, where it goes next, and the code in eight pieces.
- [About Steven](?view=docs&doc=about): who built it.
- [Resume](/resume.pdf).

## Five minutes

1. Press **Files** above and open a folder. Watch the address bar: the folder is in it. Copy the
   address into a new tab and you are in the same folder.
2. Type `*.csv` in the search box. The totals line counts what matched; the corner says how long
   the server took.
3. Sort by size by pressing the column heading. That is in the address too.
4. Upload a file (or drop one on the table), then download it back, then delete it.
5. Come back here and read the records in order. Each is one decision: what was asked, what was
   chosen, what it cost, and the code it decided about, read from this build as you read.

## Where things are

| Folder | What is in it |
| --- | --- |
| `Data/` | The wire: records, no behaviour. |
| `Domain/` | The rules: the home directory's guard, the search pattern, the totals. Pure. |
| `Application/` | The use cases behind one port, `IFileStore`. |
| `Infrastructure/` | The disk, behind the port. |
| `Controllers/` | The routes, a line or three each, and the problem-document handler. |
| `Composition/` | The registrations and the pipeline that `Program.cs` calls, one line each, as TheYard's are. |
| `Library/` | The documents the app serves: the catalogue, the live-sample expander, the version reader. |
| `src/` | The page, in TypeScript: `lib` (pure), `ui` (renders), `main.ts` (the shell). |
| `wwwroot/` | What the browser loads: `index.html`, two stylesheets, and `js`, which `tsc` wrote from `src/`. |
| `tests/` | xUnit under `TestProject.Tests`, `node --test` under `js`. |
| `docs/` | These documents. |
| `sample-home/` | What is browsed when nothing else is configured. |

## The records

1. ADR-001 The starter, kept as given
2. ADR-002 One project, four folders, dependencies inward
3. ADR-003 The line a path cannot cross
4. ADR-004 The wire
5. ADR-005 State lives in the URL
6. ADR-006 TypeScript, organised
7. ADR-007 The dialog widget
8. ADR-008 Performance, measured
9. ADR-009 The rules a change has to pass
10. ADR-010 The palette, borrowed from TheYard
11. ADR-011 Built with AI
12. ADR-012 Documents served by the app, with live code

If you read three: ADR-003, ADR-005 and ADR-009.
