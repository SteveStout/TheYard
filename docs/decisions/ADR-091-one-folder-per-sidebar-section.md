# ADR: One folder per sidebar section

Status: accepted, 2026-10-06, shipped as 1.0.3.86. The documents moved into these folders in 1.0.3.84; this record writes down the rule they follow from now on.

## In plain words

Every document in this repository lives in the folder named for the sidebar section that shows it, so the `docs/` folder on GitHub reads the same way as the sidebar on the site. The decision records live in `docs/decisions/`. Only the changelog and the pictures stay in the root of `docs/`, because the deploy and the drawing scripts read them there.

What that is worth: a developer finds a document on GitHub where the site says it is, and knows where a new one goes without asking, and the organization keeps a library that stays in order as it grows, because the build fails when a document lands in the wrong folder.

## Context

Until 1.0.3.84 every document sat flat in `docs/`: ninety records and twenty-eight pages in one list of a hundred and eighteen files. The site already grouped them into sections in its sidebar (`src/library/sections.ts`), so a reader on the site saw a library and a reader on GitHub saw a long scroll with no shape. Steve asked for a folder structure on 6 October, approved the tree below the same day, and asked for the rule to be written down as a record so it is followed from here on.

An earlier record kept the records flat on purpose (ADR: Style, enforced): at twenty-seven files a move bought nothing a reader could see. At a hundred and eighteen it did, and that record carries an addendum saying when it stopped being true.

## Decision

**One folder per sidebar section that holds documents, named for the section in lower case with hyphens, and the decision records in `docs/decisions/`.** The tree, as it stands at this version:

```text
docs/
  CHANGELOG.md          stays in the root: both deploys read the version from it
  images/               stays in the root: the drawing scripts and every picture name it
  about/                About
  author/               Author
  app-architecture/     App Architecture
  sql-vs-cosmos/        SQL vs Cosmos DB
  performance/          Performance
  style/                Style
  site-traffic/         Site traffic
  hosting/              Hosting
  built-with-ai/        Built with AI
  ci-cd/                CI/CD
  best-practices/       Best Practices
  decisions/            Decision Records, ADR-001 onward
```

API Reference and Diagrams have no folder, because they hold links and drawings and no markdown. The README stays at the root of the repository, where GitHub shows it.

**The slug is the address, and the folder is not.** A document is served by its slug (`?doc=sealed`, `/api/docs/sealed`), and `DocumentationCatalog.cs` maps each slug to its path. Moving a file changes its path in the catalogue and never its slug, so a link posted anywhere keeps working. Links to the site are written as `?doc=` slugs for that reason; links to GitHub name the file's path and move with it.

## How to follow it

Adding a document:

1. Put the file in the folder of the section that will show it. A new section gets a new folder, named for the section, and a row in the map at the top of `DocsFolderTests`.
2. Give it a slug in `DocumentationCatalog.cs` and an entry in `src/library/pages.ts`, or, for a record, in the last run under `src/library/decisionRecords/`.
3. Add its row to the section in `src/library/sections.ts`.

Moving a document to another section moves its file to that section's folder in the same commit, with `git mv` so its history follows it. A record is never moved out of `docs/decisions/`, and its number never changes.

Nothing else goes in the root of `docs/`. If a new file has to sit there because a tool reads it there, the tool's reason is written in this record and the file is added to the root list in `DocsFolderTests` in the same commit.

## What holds it

`DocsFolderTests` fails the build when any of this slips:

- every document the sidebar offers sits in the folder of its section, read from `sections.ts`, the two document lists and the catalogue;
- every markdown file under `docs/` is one the catalogue serves, so a file nobody can open from the site cannot hide in a folder;
- the root of `docs/` holds only the section folders, `CHANGELOG.md` and `images/`, and every section folder holds at least one document.

```live path=api/TheYard.Tests/DocsFolderTests.cs region=folders
```

The rule is a row in the table of rules a change has to pass (ADR: The rules a change has to pass), and the tests that count and number the records read `docs/decisions/` (ADR: The Decision Records index).

## Consequences

- A reader on GitHub opens `docs/` and sees thirteen folders and the changelog instead of a hundred and eighteen files.
- Every tool that reads a document by path follows the folders. When the move shipped, one did not: the preview card's drawing script still counted records in the old folder and drew zero, and the test that holds the card's count failed the gate before anything was pushed. Anything new that reads `docs/` reads it through the catalogue or names the folder.
- A document whose section changes costs a `git mv` and a catalogue line, and its address stays the same.

## Where it sits

Outside the rings: this is how the repository's documents are kept, not code the application runs. The catalogue in the host maps slugs to these paths, and `DocsFolderTests` in TheYard.Tests holds the layout.

## Files

- [`api/TheYard.Tests/DocsFolderTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/DocsFolderTests.cs): the test that holds the folders, with the section-to-folder map.
- [`api/TheYard.Api/DocumentationCatalog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/DocumentationCatalog.cs): every slug and the path it serves.
- [`src/library/sections.ts`](https://github.com/SteveStout/TheYard/blob/main/src/library/sections.ts): the sidebar sections the folders follow.
- [`docs/`](https://github.com/SteveStout/TheYard/tree/main/docs): the folders themselves.
