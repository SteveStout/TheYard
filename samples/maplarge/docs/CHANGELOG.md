# Changelog

One line per shipped version, newest first. The footer reads its version from the top line here,
so this file and the page cannot disagree (ADR-012).

- 1.0.0.3 The filesystem's own refusals (a read-only file inside a folder being deleted, a file held open) answer as a 409 or 403 problem document instead of a 500; an upload batch goes on after a name that exists is overwritten or skipped; a dropped folder is named rather than silently ignored.
- 1.0.0.2 The sample runs on .NET 10 with TheYard, the served documents folder is `Library/` so `docs/` is one folder on every filesystem, and a repository link is checked with the exact case it is written in.
- 1.0.0.1 The Shed: browse, search, upload, download, folders, move, copy, delete, the address as the only state, twelve records served with live code, 103 xUnit and 20 node tests.
