# ADR: The changelog

Status: accepted, 2026-09-02, shipped with 1.0.0.14, the first version to
write its own line.

## Context

Fourteen versions reached the live site in three days, and the only record
of what each one was lived in the commit history and the day's session
logs. The footer says which build is running (ADR: Version in the footer),
but a visitor who wants to know what changed between 1.0.0.9 and 1.0.0.13
had to read commits. His ask, in his words: just one file with the change
version number and a single sentence summary.

The complication is where the number comes from. Since ADR: The deploy
pipeline, a version is minted by the deploy counter, 1.0.0.(11 + Deploy run
number), at the moment the pipeline runs. Nobody types it, so nobody is
standing there to write the sentence when it becomes known.

## Decision

- **One file, docs/CHANGELOG.md**, newest first, one line per shipped
  version: the number, the date, one sentence. A hiring manager scans it in
  thirty seconds, and that is the whole design.
- **One sentence, high level.** The commit history holds the detail and the
  decision records hold the why. A line that needs a second sentence is a
  line trying to be a commit message.
- **Retroactive naming follows the footer.** The manual ships v1 through v11
  are 1.0.0.1 through 1.0.0.11, the convention the footer adopted in
  1.0.0.9 and the pipeline continued from 1.0.0.12. The early lines were
  mined from the session logs and the commit history, and a commit that
  went live inside a later version belongs to that version's line.
- **Its own menu, Changelog, one item.** It joins the header order, so the
  phone drawer lists it with no code of its own, and the endpoint is
  /api/docs/changelog. This record sits under Best Practices with the other
  documentation decisions.

## The maintenance rule

The number is minted by the deploy counter, so the line is written one ship
ahead of the mint:

1. The commit that ships a change adds its line at the top of the file,
   numbered one past what the footer shows. Deploys are serialized and every
   green merge to main deploys, so the next number is known before the push.
2. The push is the deploy. The Deploy workflow's version step checks that
   the file names the number it just computed and prints a warning into the
   run when it does not. It never stops a deploy over a sentence.
3. When a red CI run consumes a run number, the displayed version skips and
   the line is off by one. The next ship corrects it. The API test keeps the
   file honest in the ways a test can: newest first, no repeats, every line
   in the one-line shape, no em dash anywhere.

Ceremony per ship: one line in one file, inside the commit that earns it.

## In the code

The endpoint, in
[`api/TheBlock.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/Program.cs):

```csharp
app.MapGet("/api/docs/changelog", () =>
    Results.Text(File.ReadAllText(FindUpward(AppContext.BaseDirectory, Path.Combine("docs", "CHANGELOG.md"))), "text/markdown"));
```

The menu and the header order, in
[`src/components/DocsMenu.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/DocsMenu.tsx);
the desktop header and the phone drawer both render from `MENU_ORDER`, so
the new menu appeared on both surfaces from this one entry:

```ts
changelog: {
  label: 'Changelog',
  items: [{ key: 'changelog' }],
},
```

```ts
/** Header order, left to right. The desktop header and the phone drawer both render from it. */
export const MENU_ORDER: MenuVariant[] = ['hosting', 'cicd', 'practices', 'changelog', 'about'];
```

The warning in the Deploy workflow's version step,
[`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml):

```bash
if grep -q "^- \*\*1\.0\.0\.$N\*\*" docs/CHANGELOG.md; then
  echo "docs/CHANGELOG.md names 1.0.0.$N"
else
  echo "::warning file=docs/CHANGELOG.md::No changelog line for 1.0.0.$N; add it with the next ship (ADR-012)"
fi
```

The shape test,
[`api/TheBlock.Tests/ChangelogTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/ChangelogTests.cs):
every entry line matches `- **1.0.0.N** (date): sentence.`, the versions
descend with no repeats, the bottom line is 1.0.0.1, and the file carries
no em dash.

## What this replaced

Nothing. The alternative considered was generating the changelog from commit
subjects at deploy time, which gives a line per commit rather than per
version, in the voice of whoever wrote the commit, worded at the moment of
the push. A hand-written sentence per version costs one line of typing and
reads like a person wrote it.

## Consequences

- Every ship from here carries its sentence; a version without a line shows
  up as a warning in the Deploy run, visible to anyone who opens it.
- The file is the first place to look for what changed, and the commit
  history stays the second.
- The predicted number can be wrong by one after a red CI run. The rule
  accepts that and fixes it on the next ship rather than adding a second
  workflow to rewrite the file.
