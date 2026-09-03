# ADR: The version comes from the changelog

Status: accepted, 2026-09-03, shipped as 1.0.0.41. This supersedes the version
formula in ADR: Version in the footer, ADR: The deploy pipeline and
ADR: The changelog. Those records keep their reasoning; the number is computed
differently now.

## Context

The displayed version was `1.0.0.(11 + deploy run number)`. The changelog's top
line named the number the next deploy would produce, written by hand, one past
whatever the footer showed.

ADR: The changelog already knew the weakness and wrote it down:

> The deploy's changelog check would not have caught it. It asks whether a line
> exists for the version being shipped, which was true; it cannot know that a
> line further up describes a version that never will. The cheap guard is
> procedural.

The procedural guard lasted about a day.

On 2026-09-03 the style job's first CI run went red. Deploy fires on
`workflow_run` and is gated by a conditional, so the run started, skipped, and
consumed run number 28. The fix went out on the next green CI as run 29, which
made it 1.0.0.40, while the changelog line written with that commit said
1.0.0.39. The live footer and the file that documents the live footer
disagreed, in a project whose stated selling point is that they cannot.

Nothing was broken. A visitor comparing the footer to the changelog would have
found a portfolio site contradicting itself, which is worse than broken.

## Decision

**The changelog's top line is the version.** The deploy reads it, and there is
no offset and no run counter in it any more.

```
- **1.0.0.41** (2026-09-03): one sentence.
   ^^^^^^^^^^ this is what the footer shows and what tags the image
```

Two things follow immediately, and both are the point:

- A skipped or repeated run number cannot move the version, because the run
  number no longer appears in it.
- A ship that forgets its changelog line fails the deploy instead of shipping
  a number nothing describes. The check that used to warn now decides.

**The validation is a hard failure, in two parts.** No parseable line at the
top stops the deploy. A top line that is not strictly above the line below it
stops the deploy, which is the "forgot to bump" case: the second line is by
definition the last thing that shipped.

**The API test keeps the same file honest from the other side.** It already
asserted the shape, the descending order, no repeats and no em dash. It now
also asserts the top line is above the second, which is the same rule the
workflow enforces, so a mistake is caught in CI a minute before it would be
caught by a failed deploy.

**Numbers may still skip, and 1.0.0.39 is the first one that did.** That gap is
kept rather than renumbered. A version that never ran is not a version, and
rewriting history to hide a red build is the opposite of what this file is for.

## In the code

The version step (`.github/workflows/deploy.yml`):

```live path=.github/workflows/deploy.yml region=compute-version
```

The refusal (`.github/workflows/deploy.yml`):

```live path=.github/workflows/deploy.yml region=changelog-check
```

The same rule from the other side (`api/TheYard.Tests/ChangelogTests.cs`):

```live path=api/TheYard.Tests/ChangelogTests.cs region=version-order
```

## Consequences

- The footer and the changelog cannot disagree. Not "should not": the string in
  the footer is read from the file that documents it, so a disagreement would
  require the deploy to have used a different file.
- Writing the changelog line stops being a convention and becomes a step of the
  build. Skipping it is a red deploy, not a warning nobody reads.
- The image tag follows the same number, so `v41` in the registry is the image
  that says 1.0.0.41, and no run of the workflow can produce a tag that already
  exists without also failing its version check.
- The `OFFSET: 11` environment variable is gone. It was the last thing in the
  pipeline that had to be remembered rather than derived.
- One thing this deliberately does not do is derive the version from a git tag.
  That would be the right answer for a library with releases. Here the changelog
  is already mandatory, already tested, and already served to the reader, so it
  is the artefact with the fewest ways to be wrong.

## Files

- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml): the version step and the refusal.
- [`api/TheYard.Tests/ChangelogTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ChangelogTests.cs): the same rule, enforced a minute earlier.
- [`docs/CHANGELOG.md`](https://github.com/SteveStout/TheYard/blob/main/docs/CHANGELOG.md): now an input to the build.
- [`docs/ADR-012-changelog.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-012-changelog.md): the record that predicted this failure and chose the procedural guard.
- [`docs/ADR-005-version-footer.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-005-version-footer.md), [`docs/ADR-009-deploy-pipeline.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-009-deploy-pipeline.md): where the old formula was written down.
