#!/usr/bin/env bash
# catch_list.sh [<base>]
#
# Does:      The catch list (ADR: The gate runs where the memory is, the addendum on the catch list): the fast,
#            store-free checks behind the reds the gate has thrown, run on the filled commit before a claim. The em dash
#            scan and the ratchet are the gate's own commands; the size of the version is held to its band; the og card
#            is checked as drawn from this commit; and the specs and suites the change touches are named, so the lane
#            runs them through yard-test. Prints its own seconds against a two-minute cap; exits 1 when a check is red.
# Does not:  Run a Cosmos pass, a browser pass or a second full run: the full run is the gate's. A check that would
#            take the list past its cap does not join it.
# Used by:   scripts/preflight.sh, which every lane runs before its claim.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
base=${1:-origin/main}
cap=120
start=$SECONDS
red=""
catch() {
  local name=$1
  shift
  local out
  if out=$("$@" 2>&1); then
    printf '%-12s ok   %s\n' "$name" "$(tail -n 1 <<< "$out")"
  else
    printf '%-12s RED\n' "$name"
    sed 's/^/    /' <<< "$out" | head -n 20
    red="$red $name"
  fi
}

catch em-dashes bash scripts/emdash_scan.sh
catch ratchet bash scripts/quality_ratchet.sh
catch size bash scripts/pr_size.sh "$base"
# A heal branch (GATE_LANE heal-*) may not touch the outside self-heal list; any other lane passes.
catch heal-outside python3 scripts/heal_outside.py --base "$base"
# The version card is drawn from the commit's own changelog line; one drawn from another line turned a gate red.
catch og-card bash -c '[ -d node_modules/@playwright/test ] || { echo "og card: not judged, node_modules is not installed here"; exit 0; }; node docs/images/og.mjs > /dev/null && git diff --quiet -- docs/images/og.svg public/og.png || { echo "the og card is not drawn from this commit: run node docs/images/og.mjs and amend"; git checkout -q -- docs/images/og.svg public/og.png; exit 1; }'

# #region touched-specs
# The suite the change touches is the lane's to run (CLAUDE.md, Testing): each path a red came from names its spec.
changed=$(git diff --name-only "$base" HEAD)
say() { echo "  run: $1 (touched: $(grep -m1 -E "$2" <<< "$changed"))"; }
echo "the specs and suites this change touches:"
grep -qE '^(api/TheYard\.Api/Features/Admin/Cloud/|src/components/admin/)' <<< "$changed" && say "yard-test <branch> e2e for tests/e2e/cloud.spec.ts" '^(api/TheYard\.Api/Features/Admin/Cloud/|src/components/admin/)'
grep -qE '^functions/' <<< "$changed" && say "dotnet test functions/jobhunt.Tests" '^functions/'
grep -qE '^(src/|index\.html|vite\.config)' <<< "$changed" && say "npm run build && npx vitest run tests/budgets (the byte budget)" '^(src/|index\.html|vite\.config)'
grep -qE '^api/' <<< "$changed" && say "yard-test <branch> api" '^api/'
# #endregion touched-specs

took=$((SECONDS - start))
echo "catch list: ${took} s of its ${cap} s cap"
[ "$took" -le "$cap" ] || echo "catch list: past its cap; the slowest check leaves the list"
[ -z "$red" ] || {
  echo "catch list RED:$red"
  exit 1
}
