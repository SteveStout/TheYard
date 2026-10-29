#!/usr/bin/env bash
# preflight.sh
#
# Does:      The checks the gate runs that a shipping machine can also run, in under three minutes and inside
#            8 GB: dotnet format, Prettier, oxlint and the type check, each named with the first lines of its
#            output when it is red, and then one line saying whether this version changes .github/workflows
#            and whether the secret PROMOTE_TOKEN will carry it. Exits non-zero on any red.
# Does not:  Run the browser suite or the two passes on Cosmos DB. Those need the 16 GB runner, which is why
#            the gate moved off the laptop in the first place (ADR-098, ADR-068). Read, print or take a token
#            value: the secret is read by NAME from the repository's secret list and nowhere else.
# Used by:   Whoever ships, before a push of gate/<version> (CLAUDE.md, docs/ci-cd/CICD.md). A red preflight
#            is never pushed.
#
# Runs on the laptop that ships and on the Linux machine beside it. A check this machine cannot run says so
# and is UNKNOWN; nothing here is ever assumed green.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)" || exit 1
out=$(mktemp -d)
red=""

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
check prettier npm run format:check
check lint npm run lint
check tsc npx tsc -b
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
    echo "  Promote this one by hand once the gate is green:"
    echo "    gh run download <run> -n gate-raw -D raw && bash scripts/promote.sh raw <gate seconds> <version> <run>"
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
echo "preflight green: none of these four will turn the gate red"
# #endregion the-verdict
