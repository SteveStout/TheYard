# Coding and Commenting Style

The rules this code is written to, carried over from TheYard's page of the same name and trimmed
to what this project has: C#, TypeScript, CSS and markdown. The architecture record (ADR-002)
says which way the dependencies point; this one says what the code looks like once it is there.

The mechanical half is enforced by `.editorconfig` and the compiler settings, not by review:
indentation, line endings, using order, `var` usage, unused usings, braces. Review is for the half
a tool cannot check.

## Naming

- C#: `PascalCase` for types, methods and properties, `camelCase` for locals and parameters,
  `_camelCase` for private fields. A port reads as the thing it provides (`IFileStore`), not as
  the implementation.
- TypeScript: `camelCase` for values and functions, `SCREAMING_SNAKE` for module constants
  (`SEARCH_DEBOUNCE_MS`, `DEFAULTS`).
- The wire is snake_case end to end (`folder_count`, `took_ms`). Nothing is renamed in transit.
- A test name is a sentence: `A_folder_cannot_be_moved_or_copied_into_itself`,
  `Upload_past_the_limit_is_413_before_a_byte_is_read`. A failing run should read like a report.
- Files are named for what they hold, one main type each: `HomePath.cs`, `urlState.ts`.

## Layering

- Dependencies point inward: Data, Domain, Application, Infrastructure, then Documentation,
  Controllers and Composition on the outside. A reference that points outward is code in the wrong
  folder, and `OnionTests` says so by reading the compiled app (ADR-013).
- Domain code is pure: no `DateTime.Now`, no filesystem, no HTTP.
- The host binds and delegates. A rule in `Program.cs` is a bug in layering.
- The browser holds no business rules. Totals, sort order within a reply, and every refusal come
  from the server.
- `src/lib` is pure TypeScript with no document; `src/ui` renders; `wwwroot/js` is what `tsc` wrote
  and is not edited by hand.

## Comments

The rule is **why and how, never what**. The code already says what it does; a comment earns its
line by saying why this way, what breaks otherwise, or what a reader could not know from the syntax.

```csharp
// EnumerateFileSystemInfos hands back each entry with its attributes and
// size already read from the directory listing, so a folder of ten
// thousand files costs one enumeration, not ten thousand stat calls.
```

Four habits:

- A public C# member gets a `<summary>` always; the build fails without one.
- A file that implements a decision names the record by its file:
  `(more in docs/ADR-003-the-line-a-path-cannot-cross.md)`.
- A comment that explains a workaround says what would happen without it.
- Code shown in a record is documented by that record, and carries teaching comments for a reader
  meeting the pattern for the first time.

## Tests

- Pure rules get xunit unit tests; anything needing the host gets a test through
  `WebApplicationFactory`; the folder rings get NetArchTest over the compiled app; anything pure in
  TypeScript gets `node --test` over the compiled module.
- Time is anchored, never `Now`. A test that needs a disk gets a `TempHome` of its own and deletes
  it.
- A test that reads the repository (the records, the stylesheet) walks it through `ProjectFolder`,
  so the list of folders to skip is written once.

## Formatting

- Four spaces in C#, two in TypeScript, CSS, JSON and YAML; the compiled JavaScript keeps the
  compiler's four. UTF-8, LF, final newline.
- Lines wrap around 120 characters in C# and 100 in TypeScript and markdown.
- One statement per line; braces always.
- Prose in this project, records and commit messages included, uses no em dashes. `NoEmDashTests`
  counts them.

## Files

- [`.editorconfig`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/.editorconfig): the mechanical rules.
- [`docs/ADR-002-one-project-four-folders-dependencies-inward.md`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/docs/ADR-002-one-project-four-folders-dependencies-inward.md): the layers these rules protect.
- [TheYard's Coding and Commenting Style](https://github.com/SteveStout/TheYard/blob/main/docs/STYLE.md): the page this one is trimmed from.
