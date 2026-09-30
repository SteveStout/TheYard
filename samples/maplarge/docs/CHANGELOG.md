# Changelog

One line per shipped version, newest first. The footer reads its version from the top line here,
so this file and the page cannot disagree (ADR-012).

- 1.0.0.17 The refusal the API throws is ApiRefusalException and the handler that turns every refusal into a problem document is ProblemResponseHandler; "Browser" could mean the web browser or the file browser, and the documentation controller throws it too; routes, JSON and behaviour are unchanged.
- 1.0.0.16 Every name says what it is: DocsController is DocumentationAndVersionController, the Library folder is Documentation with DocumentationCatalog in it, Data/Entries.cs is split into ApiResponses.cs, ApiRequests.cs and FileStoreEntry.cs, and the page's src/ui/dom.ts is elements.ts with buildElement, buildFromMarkdown and replaceContents; the tests follow (DocumentationTests, ProjectRulesTests, ProjectFolder, NoEmDashTests); routes, JSON and behaviour are unchanged.
- 1.0.0.15 The registration for the Docs tab and the footer's version is DocumentationAndVersionRegistration, named for both things it sets up, and its comments say where each value really comes from; "The Shed, explained" shows it as piece 3 of nine.
- 1.0.0.14 The file store's comments say where the files belong in production, Azure Blob Storage or another cloud file storage service, because a container's disk does not last; "The Shed, explained" walks the code in eight pieces, with the file browser's registration second, after Program.cs.
- 1.0.0.13 Every comment in the code says what the code does, how and why, in plain words that stand on their own; no line of code changed.
- 1.0.0.12 "The Shed, explained" names every file by its path: each code piece carries its full repository path, and the brief's table says where each item lives under samples/maplarge.
- 1.0.0.11 Program.cs reads as a table of contents the way TheYard's does: the registrations and the request pipeline move, unchanged, into Composition/, Program.cs calls each one on a line that names its file, and "The Shed, explained" shows it as the first of the code's seven pieces.
- 1.0.0.10 The README reaches the running site: the project file now publishes it, so "The Shed" in the Docs tab and "about this build" in the footer open it there instead of a 409, and a test holds every catalogue document to the publish list.
- 1.0.0.9 About Steven carries the Yard's house-plants section, below the rabbits as on the Yard, so the two About pages read the same; Start here names `src/` and ADR-012 names About Steven in the catalogue.
- 1.0.0.8 Leaving the Docs tab drops the document from the address, so a link copied from the Files tab says only what that tab shows.
- 1.0.0.7 The page's comments follow TheYard's standard: a doc block opens every file, the regions the records show carry teaching comments, and ADR-006 opens with the instruction that produced it.
- 1.0.0.6 "The Shed, explained" is two pages: the second is the code in six pieces, read from the repository at 1.0.0.5.
- 1.0.0.5 The page is TypeScript in `src/`, compiled by `tsc` alone to the committed modules in `wwwroot/js`, with the wire declared once in `types.ts` and a rule test on the front end (ADR-006); About Steven, the resume and "The Shed, explained" are served by the site and linked from the header, Start here and the README; a document can carry a photograph; a media query left open let the hidden Docs pane show under the file table on a desktop, and a document's headings stacked inline, both fixed.
- 1.0.0.4 The footer's link opens the README in place, and an opened document hands focus to its title.
- 1.0.0.3 The filesystem's own refusals (a read-only file inside a folder being deleted, a file held open) answer as a 409 or 403 problem document instead of a 500; an upload batch goes on after a name that exists is overwritten or skipped; a dropped folder is named rather than silently ignored.
- 1.0.0.2 The sample runs on .NET 10 with TheYard, the served documents folder is `Library/` so `docs/` is one folder on every filesystem, and a repository link is checked with the exact case it is written in.
- 1.0.0.1 The Shed: browse, search, upload, download, folders, move, copy, delete, the address as the only state, twelve records served with live code, 103 xUnit and 20 node tests.
