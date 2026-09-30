# The Shed

A file and folder browser: an ASP.NET Core 10 API over one configurable home directory, and a
single page in TypeScript, with no framework, that browses, searches, uploads, downloads, makes
folders, moves, copies and deletes, inside a dialog, with the whole state of the page in the
address bar.

Built for MapLarge's developer test project (September 2026) on the starter they sent, in the
working method of [TheYard](https://theyard.stevenstout.biz), whose repository this folder lives
in. **Live:** [theshed.stevenstout.biz](https://theshed.stevenstout.biz). The running version and
commit: [/api/version](https://theshed.stevenstout.biz/api/version).

**Reviewing it?** Three things, one click each, all served by the site itself:

- [The Shed, explained](https://theshed.stevenstout.biz/the-shed-explained.pdf): six pages, the
  numbers, the quick start, the choices and why, where it goes next, and the code in nine pieces.
- [About Steven](https://theshed.stevenstout.biz/?view=docs&doc=about): who built it.
- [Resume](https://theshed.stevenstout.biz/resume.pdf).

## Run it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), the same as TheYard,
and, for the TypeScript build and its tests, Node 20 or newer.

```
dotnet run
```

Then open the address it prints (`http://localhost:5120`). It browses `sample-home/` until you
point it at a folder of your own:

```
FILES__HOME=C:\some\folder dotnet run
```

or set `Files:Home` in `appsettings.json`. Visual Studio 2022, Rider and VS Code open
`TestProject.sln`.

## Tests

```
dotnet test
npm install && npm test
```

The page is TypeScript in `src/`, compiled by `tsc` alone into `wwwroot/js`, and the output is
committed, so `dotnet run` needs no Node at all. After a change under `src/`:

```
npm run build
```

| Suite | Count | What it covers |
| --- | --- | --- |
| xUnit | 112 | The path guard and the search pattern (pure); the use cases over an in-memory store; every route and every refusal through the real host over a temp home; the served PDFs; the records, the live fences, the links and the publish list; the repository rules (layering, sealed, no em dash, no raw colour, the rules table, the front end); a measured search over 10,000 files. |
| node --test | 21 | The address parser and serializer, the byte and date formatting, the markdown reader (a `<script>` arrives as text, an image keeps its alt text). |

The build treats warnings as errors and a public member without a summary is a warning.

## The API

Every path is relative to the home, forward slashes; every failure is an RFC 9457 problem
document (ADR-004).

| Route | Does |
| --- | --- |
| `GET /api/files?path=` | One folder: folders, files, totals, `took_ms`. |
| `GET /api/files/search?path=&q=&limit=` | Everything under a folder whose name matches a substring or a glob (`*.md`, `report?`), capped, with `truncated`. |
| `GET /api/files/download?path=` | The bytes, as an attachment, ranges accepted. |
| `POST /api/files/upload?path=&overwrite=` | Multipart files into a folder; 409 on a name that exists unless `overwrite=true`; 413 past the limit. |
| `POST /api/files/folder?path=&name=` | A new folder. |
| `DELETE /api/files?path=` | A file, or a folder and its contents. Home itself is refused. |
| `POST /api/files/move`, `POST /api/files/copy` | `{ "from": "a/b.txt", "to": "c/b.txt" }`. A folder is never put inside itself. |
| `GET /api/docs`, `GET /api/docs/{slug}` | The documents below, as markdown with live code expanded. |
| `GET /api/version` | The version from the changelog and the commit. |

## Deep links

The page's state is the query string and nothing else (ADR-005):
`/?path=reports/2026`, `/?q=*.csv&sort=size&dir=desc`, `/?view=docs&doc=adr-003-the-line-a-path-cannot-cross`.
A bare address is the page with the dialog closed; any state opens it.

## The records

Twelve decision records, served from the running app under the Docs tab and readable here in
[`docs/`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/docs). Each is one
decision: what was asked, what was chosen, what it cost, and the code it decided about, read from
the build at request time. Three to read first:

- [ADR-003, the line a path cannot cross](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/docs/ADR-003-the-line-a-path-cannot-cross.md): the security model, one class.
- [ADR-005, state lives in the URL](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/docs/ADR-005-state-lives-in-the-url.md): deep links, done fully.
- [ADR-009, the rules a change has to pass](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/docs/ADR-009-the-rules-a-change-has-to-pass.md): every standing rule beside the test that holds it.

## Layout

```
Program.cs            the host, a table of contents: one line per part, each naming its file
Data/                 ApiResponses, ApiRequests, FileStoreEntry: sealed records, no behaviour
Domain/               the rules: HomePath, NamePattern, ViewTotals (pure)
Application/          FileBrowser, the use cases, behind IFileStore
Infrastructure/       PhysicalFileStore, the disk
Controllers/          FilesController, DocumentationAndVersionController, ProblemResponseHandler
Composition/          the registrations and the pipeline Program.cs calls, as TheYard's are
Documentation/        DocumentationCatalog, LiveSamples, VersionReader: the documents the app serves
src/                  the page, TypeScript: lib (pure), ui (renders), main.ts (the shell)
wwwroot/              index.html, css/tokens.css, css/app.css, js (what tsc wrote from src)
tests/                TestProject.Tests (xUnit), js (node --test)
docs/                 the records and the guides
sample-home/          what is browsed until Files:Home is set
```

Dependencies point inward, left to right in the list above, and a test holds it (ADR-002).

## What comes next

The Shed is a sample, and the parts worth keeping go back to the project it sits in:

- `HomePath`, `PhysicalFileStore` and the capped search become a Files card on TheYard's Admin tab,
  browsing the container's own logs, data and documents behind the operator's key.
- ADR-003, the line a path cannot cross, becomes a Best Practices page there, the way its
  sealed-by-default page is.
- TheYard's sidebar gets a Code Samples section that links this one, so the two read as one way of
  working on two problems.
- A committed browser test for this page (the eight steps the build's headless pass runs) is the
  first thing to add here.

## Built with AI

The shape and the tests were decided first; an AI assistant (Claude) drafted against them; a
person reads everything before it ships. The exact division is in
[ADR-011](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/docs/ADR-011-built-with-ai.md)
and [`docs/BUILT-WITH-AI.md`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/docs/BUILT-WITH-AI.md).
