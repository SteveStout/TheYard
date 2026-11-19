#!/usr/bin/env bash
# preflight.sh
#
# Does:      The ship's shape first. Since the traffic cop (ADR-137, One traffic cop for main) a lane ships
#            ready/<lane>-<sha8>: one commit on origin/main with the changelog's top line still the version placeholder,
#            which the cop fills at landing; no number is claimed and no lock is taken. A filled top line is the old
#            path, the version claim check (scripts/versions.sh), taken only while main's gate.yml has no ready/**
#            trigger and red after. Then the checks the gate runs that a shipping machine can also
#            run, in under three minutes and inside 8 GB: dotnet format, Prettier, oxlint and the type check, each
#            named with the first lines of its output when it is red, and then one line saying whether this version
#            changes .github/workflows and whether the secret PROMOTE_TOKEN will carry it. Exits non-zero on any red.
# Does not:  Run the browser suite or the two passes on Cosmos DB. Those need the 16 GB runner, which is why
#            the gate moved off the laptop in the first place (ADR-098, ADR-068). Read, print or take a token
#            value: the secret is read by NAME from the repository's secret list and nowhere else.
# Used by:   Whoever ships, before the push of ready/<lane>-<sha8> (CLAUDE.md, Shipping). A red preflight is never
#            pushed.
#
# Runs on the laptop that ships and on the Linux machine beside it. A check this machine cannot run says so
# and is UNKNOWN; nothing here is ever assumed green.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)" || exit 1
out=$(mktemp -d)
red=""

# #region the-version
# The ship's shape, checked before anything slow runs. A placeholder top line is a ready branch: one commit on the
# origin/main just read, which the cop rebases and numbers. A filled one is the old claim (scripts/versions.sh), which
# the cop closes once main carries it.
printf '%-9s ' version
placeholder="@""VER@"
top=$(grep -m1 -E '^- \*\*[^*]+\*\*' docs/CHANGELOG.md | sed 's/^- \*\*\([^*]*\)\*\*.*/\1/')
git fetch -q origin +refs/heads/main:refs/remotes/origin/main 2> /dev/null || true
if [ "$top" = "$placeholder" ]; then
  ship=ready
  if [ "$(git rev-parse -q --verify HEAD~1 || true)" = "$(git rev-parse -q --verify origin/main || true)" ]; then
    echo ok
    echo "          ready: one commit on origin/main, the numbers left for the cop to fill at landing"
  else
    echo RED
    echo "HEAD is not exactly one commit on origin/main: squash into one commit and rebase onto origin/main" > "$out/version.txt"
    red="$red version"
  fi
elif git show origin/main:.github/workflows/gate.yml 2> /dev/null | grep -qF 'ready/**'; then
  ship=ready
  echo RED
  echo "the changelog's top line is $top: a lane never picks a version, leave it as $placeholder for the cop" > "$out/version.txt"
  red="$red version"
else
  ship=gate
  if bash scripts/versions.sh > "$out/version.txt" 2>&1; then
    echo ok
    sed 's/^/          /' "$out/version.txt"
  else
    echo RED
    red="$red version"
  fi
fi
# #endregion the-version

# #region the-checks
# The same four commands the gate's dotnet-side and node-side regions run, in the same order the gate runs
# them. dotnet format is first because it is the one the gate's list of before-the-push commands has never
# named, and the first gate of 1.0.4.117 died on it.
check() {
  name=$1
  shift
  printf '%-9s ' "$name"
  if "$@" > "$out/$name.txt" 2>&1; then
    echo ok
  else
    echo RED
    red="$red $name"
  fi
}

check format dotnet format api/TheYard.slnx --verify-no-changes
check format-server dotnet format server/JobHunt.Server.slnx --verify-no-changes
check prettier npm run format:check
check lint npm run lint
check tsc npx tsc -b
# The catch list (ADR-098, the addendum on the catch list): the gate's own em dash scan and ratchet, the version's
# size against its band, the og card and the specs the change touches, under a printed two-minute cap.
check catch bash scripts/catch_list.sh
# #endregion the-checks

# #region the-workflow-line
# Which promote path this version is on, so the lane knows before it pushes whether a hand promotion is owed
# (ADR-098, the addendum of 1.0.4.119). Only the secret NAMES are read. The diff is origin/main against the
# working tree, not against HEAD as promote.sh reads it, so this answers before the commit as well as after.
echo
if ! git fetch -q origin +refs/heads/main:refs/remotes/origin/main 2>/dev/null; then
  echo "the promote path: UNKNOWN, origin could not be fetched"
elif git diff --name-only origin/main -- .github/workflows | grep -q .; then
  echo "this version changes .github/workflows:"
  git diff --name-only origin/main -- .github/workflows | sed 's/^/  /'
  if ! command -v gh > /dev/null 2>&1; then
    echo "  PROMOTE_TOKEN: UNKNOWN, gh is not on this machine"
  elif ! names=$(gh secret list -R SteveStout/TheYard 2>/dev/null | cut -f1); then
    echo "  PROMOTE_TOKEN: UNKNOWN, the repository's secret names could not be read"
  elif printf '%s\n' "$names" | grep -qx PROMOTE_TOKEN; then
    echo "  PROMOTE_TOKEN exists: the promote job pushes main itself and that push rolls the site"
  else
    echo "  PROMOTE_TOKEN does not exist: the promote job will end green with its notice and move nothing."
    echo "  Promote this one by hand once the gate is green: the hand promote of a cop run, CLAUDE.md, Shipping"
  fi
else
  echo "this version changes no workflow: the promote job pushes main itself"
fi
# #endregion the-workflow-line

# #region the-verdict
echo
if [ -n "$red" ]; then
  echo "preflight RED:$red"
  for name in $red; do
    echo "--- $name, the first twenty lines ---"
    head -n 20 "$out/$name.txt"
  done
  echo
  echo "a red preflight is never pushed"
  exit 1
fi
echo "preflight green: none of these checks will turn the gate red"
echo "the full run is the gate's: before the push, run only the suite your change touches (CLAUDE.md, Testing)"
if [ "$ship" = ready ]; then
  # The branch this commit ships on: ready/<lane>-<sha8>. No lock: the cop assigns the number inside the gate's group.
  branch="ready/${GATE_LANE:-<lane>}-$(git rev-parse --short=8 HEAD)"
  echo "the push: git push origin HEAD:refs/heads/$branch"
  echo "after the push: bash scripts/gate_pr.sh open $branch, so CodeRabbit reviews it (ADR-129); confirm a Gate run"
  echo "  names the branch (gh run list -w Gate -b $branch), then watch the cop status on $(git rev-parse --short=8 HEAD)"
  echo "  to landed or skipped: gh api repos/SteveStout/TheYard/commits/$(git rev-parse HEAD)/statuses --jq '.[0].description'"
else
  # The old path, until main carries the cop: gate/<version>-<lane>-<sha8>, the version first so every parser cuts at one hyphen.
  # shellcheck source=scripts/versions.sh
  . scripts/versions.sh
  branch="gate/$(versions < docs/CHANGELOG.md | sed -n 1p)-${GATE_LANE:-<lane>}-$(git rev-parse --short=8 HEAD)"
  echo "the push: git push origin HEAD:refs/heads/$branch"
  echo "after the push: bash scripts/gate_pr.sh open $branch, so CodeRabbit reviews it (ADR-129),"
  echo "  and bash scripts/gate_lock.sh annotate <run> $branch; a red gate releases the lock and frees its number"
fi
# #endregion the-verdict
