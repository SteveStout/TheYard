# ADR: The wire

Status: accepted, 2026-09-29.

## Context

The brief asks for a web API that returns JSON, and for counts and sizes for the current view. The
shape of that JSON is a decision a reviewer will read in the first minute, so it is written down.

## Decision

**snake_case, end to end.** `folder_count`, `size_bytes`, `modified_ms`, `took_ms`. One naming
policy on the serializer, no mapping layer, and the TypeScript declares the same names it receives
(`src/lib/types.ts`). The choice is TheYard's habit, kept so a reader moving between the two
projects sees one wire.

**Records, sealed.** Every reply type is a positional record in `Data/ApiResponses.cs`, sealed, with
a summary and a description of every field. A reply is a value; two listings with the same contents
are equal, which the tests use.

**Empty is valid, null is the error.** A folder with nothing in it answers `folders: []` and
`files: []`, never null. The one nullable field is `parent`, which is null at home because home has
no parent, and that is information.

**Bytes and milliseconds as long.** A size is bytes; an instant is milliseconds since the epoch,
UTC, the unit the browser's `Date` already uses. Formatting ("1.2 MB", "today 09:07") is the
page's job and lives in one file, `src/lib/format.ts`, so the table and the totals line agree.

**Totals are computed on the server** for exactly what the reply lists: the direct children of a
browse, the matches of a search. The page never adds up a column it might have sorted or filtered.

**`took_ms` on every browse and search**, measured around the store call, so the number the page
shows in its corner is the server's own (ADR-008).

**Every failure is an RFC 9457 problem document**, `application/problem+json`, with `status`,
`title`, `detail` and the framework's `traceId`. A use case throws `ApiRefusalException` with
the status it means (404 nothing there, 400 refused, 409 already exists, 413 too large); the home
throws `PathRefusedException`, always a 400; `ProblemResponseHandler` turns either into the document,
keeps the web server's own status when it cut a request off, gives the disk's refusals a 403 or 409
that names no path (ADR-013), and lets anything else through to the default handler, so an actual
bug is still a 500 and never dressed as a client error. An unknown route under `/api` is a problem document too
(`UseStatusCodePages` with the problem details service). The page reads `detail` first and shows the
server's sentence.

The problem document is the one place the wire is not snake_case: `traceId` is the framework's
name for its own field, and renaming a standard's field to match a house style would cost more than
it is worth.

## The routes

| Route | Reply | Refusals |
| --- | --- | --- |
| `GET /api/files?path=` | `Listing` | 400 refused path, 404 no such folder |
| `GET /api/files/search?path=&q=&limit=` | `SearchResult` | 400 nothing to look for, 404 |
| `GET /api/files/download?path=` | the bytes, as an attachment, ranges accepted | 404 no such file |
| `POST /api/files/upload?path=&overwrite=` multipart | `TransferResult` | 400 no files, 409 exists, 413 too large |
| `POST /api/files/folder?path=&name=` | `FolderEntry`, 201 | 409 exists |
| `DELETE /api/files?path=` | 204 | 400 home itself, 404 |
| `POST /api/files/move` `{from, to}` | the moved entry | 404, 409, 400 into itself |
| `POST /api/files/copy` `{from, to}` | the copied entry | 404, 409, 400 into itself |
| `GET /api/docs`, `GET /api/docs/{slug}` | the catalogue; a document as markdown | 404 |
| `GET /api/version` | `{version, commit}` | |

## Where it sits

The reply records live in Data at the center of the onion; a refusal is thrown from Domain or Application and leaves through Controllers as a problem document. It follows the I in SOLID: each reply fits the one screen that reads it, with nothing extra for the page to ignore. The cost is that Move and Copy hand back their record typed as object; a file keeps its size, yet the compiler cannot name the reply. If other teams generated clients from the API, a shared base record with a type field would be worth the extra code.

## Files

- [`Data/ApiResponses.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Data/ApiResponses.cs): every reply the API sends.
- [`Data/ApiRequests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Data/ApiRequests.cs): the move and copy body the page sends.
- [`Application/ApiRefusalException.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Application/ApiRefusalException.cs): a refusal with its status.
- [`Controllers/ProblemResponseHandler.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Controllers/ProblemResponseHandler.cs): the problem document.
- [`Controllers/FilesController.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Controllers/FilesController.cs): the routes.
- [`tests/TestProject.Tests/FilesApiTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/FilesApiTests.cs): every route and every refusal through the real host.

The handler:

```live path=Controllers/ProblemResponseHandler.cs region=handle
```

The two read routes, which is all a controller action should be:

```live path=Controllers/FilesController.cs region=browse-and-search
```
