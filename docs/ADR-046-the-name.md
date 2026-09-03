# ADR: The name, and how a rename was done without losing the history

Status: accepted, 2026-09-03. The projects are `TheYard.*` now. They were
`TheBlock.*`, and that name came from somewhere worth being straight about.

## Where the old name came from

This application began as a submission to OPENLANE's hiring challenge, in a fork
of their repository, under the name "The Block". Everything since has been built
on top of that start: the auction rules, the accounts, the relational store, the
forty-six records in this index.

So for weeks the public evidence disagreed with itself. The live site said
TheYard. The domain said TheYard. The repository said TheYard. And every project
inside it said `TheBlock`, which is the name of a submission to somebody else's
exercise. A reader who opened the solution found a different application than the
one the front door advertised, and the only explanation was in the git history of
a fork.

That is the whole reason for this change. Not aesthetics: a name that contradicts
the thing it names costs a reader trust before they have read a line of code.

## Decision

Rename every project, namespace, assembly and reference from `TheBlock` to
`TheYard`, in one change, with the history intact.

Three properties mattered more than speed.

**Git had to record renames, not deletions.** 208 files moved. If they go as a
delete and an add, `git log --follow` stops at the boundary and every file in the
application looks one day old, which for a portfolio destroys the one thing the
history was for. The directories were moved with `git mv` before any content
changed, so git recorded 208 of 208 as renames.

**The rewrite had to be byte-safe.** This repository has a standing rule against
rewriting files with encoding-blind tools, from an earlier incident where a
line-ending assumption produced a diff of every file. The content pass read each
file as bytes, preserved a byte-order mark where one existed, decoded UTF-8,
replaced, and re-encoded with the same mark:

```python
raw = open(path, "rb").read()
bom = raw.startswith(b"\xef\xbb\xbf")
text = (raw[3:] if bom else raw).decode("utf-8")
out = (b"\xef\xbb\xbf" if bom else b"") + text.replace("TheBlock", "TheYard").encode("utf-8")
```

921 occurrences across 141 files, and the resulting diff contains only the lines
that actually mention the name.

**Build output had to go first.** Seventeen `bin` and `obj` directories were
deleted before the move, because they hold generated files named for the old
assemblies. Four of them were locked by a process still running and left ghost
directories behind: `api/TheBlock.Api` with nothing in it but an `obj`. Those had
to be cleared separately, and they are the reason this record mentions it: a
rename that leaves four folders with the old name in the tree looks half done,
whatever the diff says.

## The references that are not paths

Most of the 921 are namespaces and file paths, and a compiler fails loudly if one
is missed. Four are strings, where a mistake is silent until runtime:

- **The migrations assembly.** Entity Framework is told which assembly holds the
  migrations by name, not by reference: `MigrationsAssembly("TheYard.Migrations.Sqlite")`.
  Wrong, and the application starts and then cannot find a single migration.
- **The DACPAC.** The SQL project builds `TheYard.Database.dacpac` now, and CI
  asserts that exact filename exists, because a build that quietly leaves the
  previous package on disk has already caught this project out once (ADR: Data
  first, and the database in source control).
- **The live code samples.** Records here quote source by path from the running
  build (ADR: Live code samples), and every one of those paths changed. A missed
  one renders as "Sample unavailable" rather than failing, which is why the
  browser suite asserts that phrase appears zero times.
- **The Dockerfile's entry point**, which names the built assembly.

## Something found on the way

`api/TheBlock.Migrations.SqlServer` existed on disk, was in no solution, was
tracked by nothing, and contained only an `obj` folder. It was scaffolded during
the SQL Server work and abandoned when the decision landed the other way: SQL
Server has no migrations here, because the SQL project is the authority and the
application holds no rights to alter a table. Removed.

An empty project directory that no build ever touches is invisible to every check
in this repository. It took a rename to find it, which is an argument for doing
this kind of sweep occasionally rather than an argument about naming.

## Consequences

- The solution, the namespaces, the assemblies, the container's entry point, the
  DACPAC and the site all say the same word.
- `git log --follow` still works on every file.
- Every GitHub link in every record points at a path that resolves, because the
  links were rewritten with everything else.
- The changelog's older entries now describe past versions using the current
  project names. That is a deliberate trade: the alternative is entries whose
  file links 404. This record is where the old name is written down.
- One cost, paid knowingly: anybody with a local clone gets a large rename diff
  and stale build output. There is one clone and it is the machine this was done
  on.

## Addendum, the same day: two files the rename did not touch

The rename swept 921 references to a name. Reading the root directory afterwards
found two files that were never about the name at all and were the same
inheritance:

**`WALKTHROUGH.md`** was the challenge's own interview brief. "After you submit
your project, we'll schedule a 45-60 minute conversation." It had been sitting at
the root of a public repository, explaining to anybody who opened it how a
submission to somebody else's exercise would be evaluated. Removed.

**`WORKING-NOTES.md`** was a hundred and eighty lines of my own notes from the
first build day, addressed to myself, describing among other things which parts
of the challenge's identity had been scrubbed and how. It was three days stale by
its own numbers, quoting 81 tests where there are now 279. It is a useful
document and it is not a public one; it lives with the other working files now.

And `CLAUDE.md`, the file an agent reads first to learn what it is working on,
opened with "An industrial and farm equipment auction marketplace". That rename
was considered on the first day and deliberately not done, and the sentence
describing the version that never happened stayed for three days in the worst
place in the repository for a sentence to be wrong.

A test now asserts that only this record names the challenge, and that the two
files which say what this project is agree about what it is. All three were found
by reading the root directory, which no check had ever done, and which is a
reminder that a repository has a front door as well as a source tree.

## Files

- [`api/TheYard.slnx`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.slnx): eight projects, all renamed.
- [`api/TheYard.Infrastructure/YardConnection.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/YardConnection.cs): the migrations assembly named as a string.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the copy list and the entry point.
- [`.github/workflows/ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml): the solution path and the DACPAC assertion.
- [`docs/ADR-040-database-source-control.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-040-database-source-control.md): why the DACPAC's name is checked at all.
