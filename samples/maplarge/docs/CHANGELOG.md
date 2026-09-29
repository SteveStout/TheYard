# Changelog

One line per shipped version, newest first. The footer reads its version from the top line here,
so this file and the page cannot disagree (ADR-012).

- 1.0.0.7 The page's comments follow TheYard's standard: a doc block opens every file, the regions the records show carry teaching comments, and ADR-006 opens with the instruction that produced it.
- 1.0.0.6 "The Shed, explained" is two pages: the second is the code in six pieces, read from the repository at 1.0.0.5.
- 1.0.0.5 The page is TypeScript in `src/`, compiled by `tsc` alone to the committed modules in `wwwroot/js`, with the wire declared once in `types.ts` and a rule test on the front end (ADR-006); About Steven, the resume and "The Shed, explained" are served by the site and linked from the header, Start here and the README; a document can carry a photograph; a media query left open let the hidden Docs pane show under the file table on a desktop, and a document's headings stacked inline, both fixed.
- 1.0.0.4 The footer's link opens the README in place, and an opened document hands focus to its title.
- 1.0.0.3 The filesystem's own refusals (a read-only file inside a folder being deleted, a file held open) answer as a 409 or 403 problem document instead of a 500; an upload batch goes on after a name that exists is overwritten or skipped; a dropped folder is named rather than silently ignored.
- 1.0.0.2 The sample runs on .NET 10 with TheYard, the served documents folder is `Library/` so `docs/` is one folder on every filesystem, and a repository link is checked with the exact case it is written in.
- 1.0.0.1 The Shed: browse, search, upload, download, folders, move, copy, delete, the address as the only state, twelve records served with live code, 103 xUnit and 20 node tests.
