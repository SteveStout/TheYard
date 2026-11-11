#!/usr/bin/env bash
# Run TheYard's tests on WickedAwsome so the laptop doesn't have to (ADR: The gate runs where the memory is, the
# addendum of 1.0.4.163). Installed as the box's command with:
#   install -m 755 scripts/yard-test.sh ~/.local/bin/yard-test
#   yard-test [branch] [all|api|server|web|e2e]     (defaults: main all)
# From the laptop:  ssh vincent@wickedawsome '~/.local/bin/yard-test my-branch'
# Tests a pushed branch in its own worktree ($HOME/repos/TheYard-ci); one run at a time.
# A suite whose source folder the branch does not carry is skipped and says so, never failed: a red that blocks
# nothing teaches every reader to look past red.
set -uo pipefail
BRANCH="${1:-main}"; SUITE="${2:-all}"
REPO="$HOME/repos/TheYard"; WT="$HOME/repos/TheYard-ci"
LOGS="$HOME/repos/test-logs"; mkdir -p "$LOGS"
STAMP="$(date +%Y%m%d-%H%M%S)-${BRANCH//\//_}"
export PATH="$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin" DOTNET_CLI_TELEMETRY_OPTOUT=1 CI=1

# The lock is this shell's fd 9, and only this shell's: every command below that starts a child closes it (9>&-),
# because a child that outlives the run (the Roslyn compile server a dotnet build leaves behind) would otherwise keep
# the lock held for every later run (ADR-098, the addendum of 1.0.5.8).
exec 9>"$LOGS/.lock"
flock -n 9 || { echo "Another yard-test is running; waiting..."; flock 9; }

git -C "$REPO" fetch -q origin "$BRANCH" || { echo "FAIL: branch '$BRANCH' not found on origin (push it first)"; exit 2; }
SHA="$(git -C "$REPO" rev-parse FETCH_HEAD)"   # FETCH_HEAD is per-worktree, so pin the commit here
[ -d "$WT" ] || git -C "$REPO" worktree add -q --detach "$WT" "$SHA" || { echo "FAIL: worktree add"; exit 2; }
# Build output a branch tracked is restored first, so a checkout to a branch that ignores it is not refused.
cd "$WT" && { git checkout -q -- ':(glob)**/obj/**' 2>/dev/null || true; } && { git checkout -q -- ':(glob)**/bin/**' 2>/dev/null || true; } \
  && git checkout -q --detach "$SHA" && git clean -qfdx -e node_modules || { echo "FAIL: could not prepare $WT"; exit 2; }
echo "Testing $BRANCH @ $(git log -1 --format='%h %s')"

if ! cmp -s package-lock.json node_modules/.yard-lock 2>/dev/null; then
  npm ci --no-audit --no-fund 9>&- >"$LOGS/$STAMP-npm.log" 2>&1 || { echo "FAIL: npm ci (see $LOGS/$STAMP-npm.log)"; exit 1; }
  cp package-lock.json node_modules/.yard-lock
fi

rc=0
run() { # name, source folder, command...
  local name=$1 source=$2; shift 2; local log="$LOGS/$STAMP-$name.log" t=$SECONDS
  if [ ! -d "$source" ]; then
    printf '%-5s %-6s   0s  skipped: %s/ not on this branch\n' SKIP "$name" "$source"
    return
  fi
  if "$@" 9>&- >"$log" 2>&1; then r=PASS; else r=FAIL; rc=1; fi
  printf '%-5s %-6s %4ss  %s\n' "$r" "$name" $((SECONDS-t)) "$(grep -hE '^(Passed!|Failed!)|Tests +[0-9]|^ +[0-9]+ (passed|failed|flaky|skipped)' "$log" | tr -s ' ' | paste -sd' ')"
  [ $r = FAIL ] && grep -hE '\[FAIL\]|✗|×|^ +[0-9]+\) ' "$log" | head -15
}
case "$SUITE" in all|api) run api api dotnet test api --filter 'Store!=cosmos' ;; esac
case "$SUITE" in all|server) run server server dotnet test server/JobHunt.Server.Tests ;; esac
case "$SUITE" in all|web) run web src npx vitest run ;; esac
case "$SUITE" in all|e2e) run e2e tests/e2e npx playwright test --reporter=line ;; esac
echo "Logs: $LOGS/$STAMP-*.log"
exit $rc
