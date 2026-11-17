# ADR: The version comes from the changelog

Status: accepted, 2026-09-03, shipped as 1.0.0.41. This supersedes the version
formula in ADR: Version in the footer, ADR: The deploy pipeline and
ADR: The changelog. Those records keep their reasoning; the number is computed
differently now.

## In plain words

This page makes the top line of the changelog the site's version number. The deploy reads the number from that line, which means the footer cannot disagree with the changelog. A deploy whose line is missing or not above the line below it fails instead of shipping.

What that is worth: a developer never has to remember a number or an offset, and the organization's live footer always matches the file that documents it.

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

## Where it sits

No ring: this is a deployment decision about where the version number comes from, so the onion and SOLID do not apply to it.

## Files

- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml): the version step and the refusal.
- [`api/TheYard.Tests/ChangelogTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ChangelogTests.cs): the same rule, enforced a minute earlier.
- [`docs/CHANGELOG.md`](https://github.com/SteveStout/TheYard/blob/main/docs/CHANGELOG.md): now an input to the build.
- [`scripts/versions.sh`](https://github.com/SteveStout/TheYard/blob/main/scripts/versions.sh): the claim check preflight runs, and the version reading promote.sh shares.
- [`api/TheYard.Tests/VersionClaimScriptTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/VersionClaimScriptTests.cs): the claim check run against a bare repository.
- [`scripts/fill_version.sh`](https://github.com/SteveStout/TheYard/blob/main/scripts/fill_version.sh) and [`api/TheYard.Tests/FillVersionScriptTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/FillVersionScriptTests.cs): the fill at the claim, and its test.
- [`scripts/gate_lock.sh`](https://github.com/SteveStout/TheYard/blob/main/scripts/gate_lock.sh) and [`api/TheYard.Tests/GateLockScriptTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/GateLockScriptTests.cs): the gate lock and the peer release rule, and their test.
- [`docs/decisions/ADR-012-changelog.md`](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-012-changelog.md): the record that predicted this failure and chose the procedural guard.
- [`docs/decisions/ADR-005-version-footer.md`](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-005-version-footer.md), [`docs/decisions/ADR-009-deploy-pipeline.md`](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-009-deploy-pipeline.md): where the old formula was written down.

## Addendum, 2026-09-22 (1.0.1.0): four numbers, compared as a version

Steve set the first release at 1.0.1.0, the first version he considered polished. Until then every version began 1.0.0, so the deploy read only the last number and compared it with `-le`. From 1.0.1.0 the deploy reads all four numbers off the changelog's top line and compares it with the line below using `sort -V`, so 1.0.1.0 ships over 1.0.0.187, and a repeated or lower version is refused as before. `APP_VERSION` is the version itself, and the image is tagged `v<version>` (`v1.0.1.0`), which the second site's deploy waits for by the same name. `ChangelogTests` parses each line with `System.Version`, so the test and the workflow still enforce one rule. The Consequences above describe the tags as they were through 1.0.0.187 (`v41` for 1.0.0.41) and are kept as the record.

## Addendum, 2026-11-16 (1.0.5.57): a taken number is refused before the claim, not after the gate

In plain words: before a lane pushes a version, the preflight now says whether its number is free. If the changelog's top line is not exactly one above main's, or another push already holds a gate branch of that number, it refuses in one sentence and prints the next free version, so the lane finds out in a minute instead of at the end of a half-hour gate.

Why: the number has only ever been checked by `scripts/promote.sh`, after the whole gate has run. On 20 October two lanes took 1.0.4.1 within minutes of each other; one shipped it, and the other's three versions were renumbered twice, with two rebases through conflicts in the changelog and the README and about seventy minutes of waiting. On 12 November a red gate run kept `gate/1.0.5.50` on origin, and from then until 16 November every lane's next number was that one: pushing over the branch takes a force-push, and on 15 November the force-push was refused to the lane that ran it, even with Steve's go-ahead in chat. Nothing told a lane any of this until it had pushed.

What the check does (`scripts/versions.sh`, run first by `scripts/preflight.sh`, which turns red on a refusal):

1. The changelog's top line must be filled: a line still holding the placeholder the ship fills is refused with "fill the version first". The script and its test spell the placeholder in two parts, because the fill replaces every whole one in the tree, their own files included; the first fill of this very version did exactly that and the check refused a filled line.
2. It must be origin/main's top line plus one in the fourth number. The owner's move of the third number (`a.b.(c+1).0`) passes only with `OWNER_YES` naming it, exactly as `promote.sh` allows. Anything else is refused with the next free version.
3. `git ls-remote origin refs/heads/gate/<top line>`: no branch passes; a branch at this very commit passes as the lane's own (the re-run after its own push); a branch at any other commit is refused whatever its age, with its commit, age and subject, and says that pushing over it needs a force-push with lease, which is Steve's to run or allow. Age never turns that refusal into a pass, because the push after a pass would be refused.
4. An origin that cannot be read is a refusal, never a pass.
5. Leftover gate branches at or below main are named and never block: only `gate/<top line>` is ever pushed. Nothing here deletes a branch.

`promote.sh` keeps every check it had, as the backstop after the gate, and now reads the changelog's version lines through the same file (`versions`, `next_version`) instead of a copy of its own. No version field was added anywhere and no versioning tool was adopted: the changelog is still the one source of the number. `VersionClaimScriptTests` runs the script against a bare repository for each case: a taken number, a number not one above main, the lane's own branch at HEAD, a leftover branch below main, a stacked one-commit branch filled to main plus one, an unfilled top line, an origin that cannot be read, and the owner's move with and without `OWNER_YES`.

## Addendum, 2026-11-18 (1.0.5.87): the number is filled at the claim, under the lock

In plain words: a lane no longer writes its version number into its commit before it claims. It builds its one commit with placeholders, takes the gate lock (`scripts/gate_lock.sh claim`), and only then fills the number from main's top line plus one with `scripts/fill_version.sh`, along with the date and the next free record number. Its gate branch is named `gate/<version>-<lane>-<sha8>`, for example `gate/1.0.5.76-coding1112-13c45731`. A number is taken only when it is on main or named in the held lock by another lane, never because a branch exists. A red gate releases the lock, and with it its number: the next claim takes main plus one again, and nothing waits on a branch being deleted.

Why: under the addendum above, a red gate left `gate/<number>` on origin, and that branch held the number for every lane until someone deleted or force-pushed it, which lanes are not allowed to do. Five numbers were held this way in a week. 1.0.5.50 was held from 12 November to 16 November. 1.0.5.62 went red on 16 November and was freed only because that lane's delete of its own branch happened to be allowed. 1.0.5.64 went red the same day, the delete was refused, and Steve force-pushed it. 1.0.5.74 went red at 23:04 on 16 November and held every lane for about 8 hours until the morning. 1.0.5.75 went red at 07:36 on 17 November and was freed at 07:41 only because its lane's delete went through. Each hold parked every lane behind one branch nobody could move. Filling at promote instead would be wrong: promote.sh refuses any change beyond the results files and the version card, so the number has to be in the commit the gate tests.

What changed:

1. `scripts/fill_version.sh` runs on the one commit on origin/main once the lane holds the lock. It refuses unless HEAD is exactly one commit on origin/main and the tree is clean, fills the version (main's top line plus one in its fourth number, or the owner's move of the third only when `OWNER_YES` names it), today's date in America/Chicago and the record number (one above the highest `docs/decisions/ADR-NNN-*.md` on origin/main, renaming a file whose name holds that placeholder) in every tracked file and in the commit message, redraws the version card, tightens `.github/quality-ratchet.json` to what `.github/quality.py` measures (a number only ever moves the good way), amends the commit and prints `VERSION= ADR= HEAD=`. It spells each placeholder in two parts, so it never fills its own source, and a second run changes nothing. A commit already filled for another number is refused rather than renumbered. Two lanes no longer collide on record numbers either, because the record number is filled at the same moment.
2. `scripts/gate_lock.sh` holds the lock. `claim` takes `flock` on a local file for the seconds it reads, writes and reads back the share's lock, because the share's mount caches what it lists for 30 seconds and every lane runs on one machine; it creates the lock with `set -o noclobber`, so a second writer fails instead of overwriting, and reads its own line back after two seconds. `annotate` adds `run=` and `branch=` once the run exists, since the run id exists only after the push. `release` renames the lock to `theyard-gate.released-<unix time>`. A peer may release another lane's lock only when its run concluded failure or cancelled, or concluded success with main's top line already its version; a line with no run is judged by the run `gh` lists for its commit. A green run that has not promoted, such as a version that changes a workflow and is promoted by hand, is released by its holder or by Steve, never by a peer, because a peer that claimed main plus one would take the number that promote is about to ship.
3. `scripts/versions.sh`'s claim check reads the held lock (`GATE_LOCK`, the share's `theyard-gate.lock` by default) instead of origin's branches. Absent, the number is free by the lock; another lane's lock naming the number is refused in one sentence naming the holder; the lane's own lock (`GATE_LANE`) passes; a lock folder that cannot be reached is said and not judged. Gate branches on origin are only named. Every other refusal and the next free version are kept.
4. Every reader of a gate branch's name takes the version as the text after `gate/` up to the first hyphen (`branch_version` in versions.sh, and the same cut in `scripts/coderabbit_review.py`): promote.sh, gate.yml, `scripts/gate_pr.sh` (which takes the branch, and still takes a bare version for an older `gate/<version>` branch) and the preflight's printed commands. The older name with no hyphen still reads whole.
5. gate.yml's concurrency group keeps `queue: max`, so a third push never cancels a pending gate in silence, and the `cancel-in-progress: false` line is gone: false is the default, and GitHub forbids `queue: max` beside `cancel-in-progress: true` and says nothing about false, so the pairing is never tried on the shared gate. A first job, `number`, runs `versions.sh gate` on the branch and turns the run away within seconds when its version is no longer main's top line plus one; every job that costs minutes waits on it, so two lanes that claimed one number lose under a minute, not a gate. promote.sh keeps every version check it had, as the backstop.

What holds it: `FillVersionScriptTests` (the fill of the version, the date and the record number in content, a file name and the message; the ratchet only tightened; a second run changes nothing; each refusal; the owner's move), `VersionClaimScriptTests` (a gate branch never takes a number; the held lock takes it only for its holder; a lock folder out of reach is not judged; the branch name read in both forms by the shell and by Python; the gate's first check; `next`), `GateLockScriptTests` (the peer release rule with a stand-in `gh`; the claim, a second claim refused, the annotation, a red run's release), `GateReviewWaitTests` and `CodeRabbitRuleTests` (the pull request and the review row on the new name and the old), `PromoteScriptTests` (promote reads the version off the branch up to its first hyphen), and `GateWorkflowTests` and `PromoteWorkflowTests` (`queue: max` with no `cancel-in-progress`, the number job every costly job waits on, the version read off the branch for promote, and the branch passed whole to the pull request's close).
