#!/usr/bin/env bash
# gate_cuts.sh <raw folder>
#
# Does:      The gate's two cuts, read from what the branch changed since it left main (ADR-068, the addendum on
#            the two cuts): whether dotnet format runs, and whether the two passes on Cosmos DB run or are carried
#            forward from the last version that ran them. Writes the changed paths to <raw>/touched.txt and the two
#            answers, FORMAT_NEEDED and COSMOS_NEEDED, to $GITHUB_ENV.
# Does not:  Run a check, or decide anything else: each gate job that needs a cut runs this itself, so the jobs
#            start together and none waits on another to learn what changed (ADR-098, the addendum of 1.0.5.4).
# Used by:   .github/workflows/gate.yml, the dotnet and cosmos jobs, on a checkout with the whole history.
set -euo pipefail
raw=$1
mkdir -p "$raw"
# The diff is from where the branch left main, so every commit it would ship is read, however many there are.
git fetch -q origin +refs/heads/main:refs/remotes/origin/main
git diff --name-only "$(git merge-base origin/main HEAD)" HEAD > "$raw/touched.txt"
cat "$raw/touched.txt"
env=${GITHUB_ENV:-/dev/stdout}
# #region two-cuts
if grep -qE '\.(cs|csproj|slnx|editorconfig)$' "$raw/touched.txt"; then echo "FORMAT_NEEDED=1" >> "$env"; else echo "FORMAT_NEEDED=0" >> "$env"; fi
if grep -qE '^(api/|infra/cosmos/|tests/e2e/(store-toggle|account|admin)\.spec\.ts$)' "$raw/touched.txt"; then echo "COSMOS_NEEDED=1" >> "$env"; else echo "COSMOS_NEEDED=0" >> "$env"; fi
# #endregion two-cuts
