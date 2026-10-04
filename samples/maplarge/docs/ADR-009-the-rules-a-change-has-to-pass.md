# ADR: The rules a change has to pass

Status: accepted, 2026-09-29.

## Context

Eleven other records describe decisions. A decision that is only described is one a busy week
undoes; a decision a test holds survives the week. TheYard keeps a table of its standing rules
beside the test that holds each one, and a test that reads the table and checks every name in it.
The same table, at this project's size.

## Decision

The rules a machine can check are listed here beside the test that checks them. `RuleTableTests`
reads this table, requires every name ending in `Tests` to be a class in the test assembly with at
least one test, and requires every record the table cites to exist. A rule with no test in this
table is a rule the next change will break.

| The rule | Decided in | Held by |
| --- | --- | --- |
| A path is refused before any filesystem touch if it is rooted, carries `.` or `..`, or a forbidden character, and the resolved path must stay inside the home | ADR-003 | HomePathTests |
| A search is a substring or a glob, never case-sensitive, and an empty query matches nothing | ADR-004 | NamePatternTests |
| Folders first, each sorted by name ignoring case; a search stops at its limit and says so; a folder is never moved or copied into itself; home is never deleted or moved; an upload past the limit is refused before a byte is read | ADR-002 | FileBrowserTests, FileTransferTests, FileUploadTests |
| Every route answers snake_case JSON, every failure is a problem document with a detail and a trace id, and an upload round-trips its bytes | ADR-004 | FilesApiTests |
| The home the app runs with is the configured one, and empty configuration means the sample home | ADR-003 | HomeForTests, FilesApiTests |
| Record numbers run from one with no gap; every record opens with its title, carries a status line, says where it sits just above its Files section and ends with that section; the catalogue serves every record and every guide; every live fence resolves; every repository link lands on a file; the version is the changelog's top line | ADR-012 | DocumentationTests |
| A publish carries every document the catalogue lists and every file a live code block quotes, so every record shows its code on the live site | ADR-012 | PublishListTests |
| A live block may read only a plain path under an allowed root, and a region is cut between its markers | ADR-012 | LiveSamplesTests |
| The version comes from the changelog and the commit from `.git`, and each says "unknown" rather than guessing | ADR-012 | VersionReaderTests |
| Nothing in the project carries an em dash | STYLE.md | NoEmDashTests |
| Every class is sealed, static or abstract, and the analyzer that holds the internal half is a warning with warnings as errors | ADR-001 | SealedByDefaultTests |
| A folder uses only the folders inside it, read from the compiled app: Data uses nothing outside .NET itself and Domain only Data, Application uses only Domain and Data and never touches the disk itself, Infrastructure never reaches outward, Controllers never touch the disk, the file store port, HomePath or Composition, Documentation never reaches Controllers, Composition, Infrastructure or ASP.NET Core, only Composition registers services, and every type sits in a folder a ring names | ADR-013 | OnionTests |
| Data and Domain touch no filesystem and no clock; Program maps no route itself | ADR-002 | LayeringTests |
| No C#, TypeScript or stylesheet file runs past 300 lines; `src/main.ts` is a short list, each line naming the file that holds its part | ADR-002 | FileShapeTests |
| No stylesheet but the token sheet writes a colour; every token used is declared; the font is served from this site | ADR-010 | StyleRulesTests |
| Every TypeScript module has its compiled module beside the page; `lib` never imports `ui`; no source uses `.innerHTML`; the compiler is the only dependency | ADR-006 | FrontEndRulesTests |
| Every test this table names exists with a test in it, and every record it cites exists | This record | RuleTableTests |
| Browsing a folder of a hundred files, and searching ten thousand, stay under the bars, and the numbers are printed | ADR-008 | PerformanceTests |
| The resume and the explained PDF are served as real PDFs, and About Steven is a link to TheYard rather than a copy | ADR-012 | PublicFilesTests |
| Every package, image and tool in the versions record is the one the project uses, and a version that is behind says why | ADR-014 | TechnologyVersionsTests |
| The address round-trips through the parser and the serializer, and a value outside its list falls back | ADR-005 | `npm test` (`node --test` over `tests/js`) |
| A `<script>` in a document arrives as text, and a `javascript:` link is not a link | ADR-006 | `npm test` (`node --test` over `tests/js`) |

Two rules are held by the compiler rather than a test: every public type and member carries an XML
summary (`GenerateDocumentationFile` with warnings as errors turns a missing one into a build
error), and no `using` is unused (IDE0005 as a warning, same reason).

## The order a change goes in

1. Read the record that governs the thing being changed; this table names it for the rules a
   machine holds.
2. If the change contradicts a record, the record changes first, as an addendum that says when it
   stopped being true rather than an edit that makes it look like it was always this way.
3. Write the test with the change, in the same commit.
4. `dotnet test` and `npm test` (after `npm run build`) run green before anything is committed. Any
   build warning is red.

## Addendum, 2026-10-03: the rings, read from the compiled app

`LayeringTests` read the folders' `using` lines as text, which missed a fully written name and let Controllers use Infrastructure. `OnionTests` (ADR-013) now holds which folder may use which by reading the compiled app with NetArchTest, so a reference in a method body fails too; `LayeringTests` keeps the two rules that read best as text. `PublicFilesTests` held its rule before this table named it, and now has a row. `TechnologyVersionsTests` (ADR-014) is new. There are thirteen other records now, not eleven.

## Where it sits

Beside the folders, in the test project. RuleTableTests reads this table, and one row is OnionTests, which holds the rings.

## Files

- [`tests/TestProject.Tests/ProjectRulesTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/ProjectRulesTests.cs): `RuleTableTests` and the other project rules.
- [`tests/TestProject.Tests/DocumentationTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/DocumentationTests.cs): the record set, the served documents and the version line.
- [`tests/TestProject.Tests/PublishListTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/PublishListTests.cs): what a publish carries.
- [`tests/TestProject.Tests/LiveSamplesTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/LiveSamplesTests.cs): the paths a live block may read and how a region is cut.
- [`.editorconfig`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/.editorconfig): the mechanical rules the compiler applies.
