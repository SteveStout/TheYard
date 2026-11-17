#!/usr/bin/env bash
# pr_size.sh [<base>]
#
# Does:      Counts the lines a version changes, added plus deleted, on the diff of HEAD against <base> (origin/main
#            when none is named), and holds the count to the band in .github/pr-size.json (ADR: A pull request of
#            AI-written code is sized to the gate). Files the promote and the fill write (the file's not_counted list)
#            and binary files are left out. Under the floor it passes only when the commit message carries a
#            "Ships-alone: <reason>" or "Fixes-main: <sha>" trailer; over the ceiling only with "Large: <reason>".
#            Prints the count, the band and the verdict in one line; exits 1 on a refusal.
# Does not:  Read the band from anywhere but the file, change the commit, or run in gate.yml: on trial it runs in
#            preflight only.
# Used by:   scripts/preflight.sh, with the catch list, and whoever ships, before the claim.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
base=${1:-origin/main}
band=.github/pr-size.json
[ -f "$band" ] || {
  echo "pr size REFUSED: $band is missing, so there is no band to hold the version to"
  exit 1
}
floor=$(jq -r '.floor' "$band")
ceiling=$(jq -r '.ceiling' "$band")
mapfile -t skip < <(jq -r '.not_counted[]' "$band")

# #region the-count
lines=0
while IFS=$'\t' read -r added deleted path; do
  [ "$added" = "-" ] && continue # a binary file
  counted=true
  for s in "${skip[@]}"; do
    case "$s" in
      */) [[ "$path" == "$s"* ]] && counted=false ;;
      *) [ "$path" = "$s" ] && counted=false ;;
    esac
  done
  [ "$counted" = true ] && lines=$((lines + added + deleted))
done < <(git diff --numstat --no-renames "$base" HEAD)
# #endregion the-count

message=$(git log -1 --format=%B HEAD)
reason() { grep -m1 -E "^$1: .+" <<< "$message" | sed "s/^$1: //" || true; }
say() { echo "pr size: $lines lines changed against ${base} (band $floor to $ceiling): $*"; }

if [ "$lines" -lt "$floor" ]; then
  why=$(reason Ships-alone)
  [ -n "$why" ] || why=$(reason Fixes-main)
  [ -n "$why" ] && {
    say "under the floor, ships alone: $why"
    exit 0
  }
  say "REFUSED, under the floor of $floor with no Ships-alone: trailer; ride in this lane's next version, or name why it ships alone"
  exit 1
elif [ "$lines" -gt "$ceiling" ]; then
  why=$(reason Large)
  [ -n "$why" ] && {
    say "over the ceiling, large: $why"
    exit 0
  }
  say "REFUSED, over the ceiling of $ceiling with no Large: trailer; split it, or name why it is one version"
  exit 1
fi
say "inside the band"
