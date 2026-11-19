#!/usr/bin/env bash
# gate_feed.sh <run-id> <out-file>
#
# Does:      What the gate's tests card and the Home page need about the version being promoted and the gate's reds,
#            read with gh and written as one JSON file the results writer folds into data/test-results.json: this
#            run's id and when its branch was pushed; how many lines the version changes (scripts/pr_size.sh's count);
#            when the version below went live (the later of its Deploy and Deploy Render runs that concluded green on
#            main's commit); and every gate run since the last promote that ended red or cancelled, with the version and
#            lane its branch names and the jobs that failed. A red gate never promotes, so nothing on main records it
#            unless this does.
# Does not:  Fail a promote: anything it cannot read is written as null or an empty list, and it always exits 0.
#            Read, print or pass a token value: gh signs in as the run or the person promoting.
# Used by:   scripts/promote.sh, before scripts/test_results.mjs.
set -uo pipefail
run=${1:?usage: gate_feed.sh <run-id> <out-file>}
out=${2:?usage: gate_feed.sh <run-id> <out-file>}
repo=${GITHUB_REPOSITORY:-SteveStout/TheYard}
cd "$(git rev-parse --show-toplevel)" || exit 0

pushed=$(gh run view "$run" -R "$repo" --json createdAt --jq .createdAt 2> /dev/null || echo "")
lines=$(bash scripts/pr_size.sh origin/main 2> /dev/null | sed -n 's/^pr size: \([0-9][0-9]*\) lines.*/\1/p')
since=$(jq -r '.ranAt // ""' data/test-results.json 2> /dev/null || echo "")

# #region the-version-below
# A version the gate promoted went live inside its own run (its deploy and deploy-render jobs); one promoted by hand went
# live in Deploy and Deploy Render runs on main's commit. The later of the two green deploys is when it was live.
main=$(git rev-parse origin/main 2> /dev/null || echo "")
below=$(jq -r '.version // ""' data/test-results.json 2> /dev/null || echo "")
live=""
if [ -n "$main" ]; then
  live=$(for flow in deploy.yml deploy-render.yml; do
    gh run list -R "$repo" --workflow "$flow" --commit "$main" --status success --limit 1 --json updatedAt --jq '.[0].updatedAt // empty' 2> /dev/null
  done | sort | tail -n 1)
fi
# The version below's own green gate run says when its branch was pushed, and, when the gate promoted it, when it went live.
promoted=""
below_pushed=""
if [ -n "$below" ]; then
  promoted=$(gh run list -R "$repo" --workflow gate.yml --status success --limit 30 --json databaseId,headBranch,createdAt 2> /dev/null |
    jq -c --arg v "$below" '[.[] | select((.headBranch | ltrimstr("gate/") | split("-")[0]) == $v)][0] // empty' 2> /dev/null)
  below_pushed=$(jq -r '.createdAt // empty' <<< "$promoted" 2> /dev/null)
fi
if [ -z "$live" ] && [ -n "$promoted" ]; then
  live=$(gh run view "$(jq -r .databaseId <<< "$promoted")" -R "$repo" --json jobs --jq '[.jobs[] | select(.name | test("^deploy")) | select(.conclusion == "success") | .completedAt] | max // empty' 2> /dev/null)
fi
# #endregion the-version-below

# #region the-red-feed
# The version is the text after gate/ up to the first hyphen; a branch named gate/<version>-<lane>-<sha8> also names its lane.
# A ready/<lane>-<sha8> branch names its lane and no version, because the cop assigns one only at landing (ADR-137): a red
# one is kept under the version the cop gave it, read below from the cop's status on its commit, "red gate as <version>".
# One with no such status (cancelled, or from before the status named it) is left out: every kept row names a version.
reds=$(gh run list -R "$repo" --workflow gate.yml --limit 50 --json databaseId,headBranch,headSha,conclusion,createdAt 2> /dev/null |
  jq -c --arg since "$since" --argjson run "${run}" '[.[] | select(.databaseId != $run) | select(.conclusion == "failure" or .conclusion == "cancelled") | select($since == "" or .createdAt > $since)
    | (if (.headBranch | startswith("ready/")) then {version: null, lane: (.headBranch | ltrimstr("ready/") | split("-") | if length >= 2 then .[0:-1] | join("-") else null end)}
       else (.headBranch | ltrimstr("gate/")) as $tail | {version: ($tail | split("-")[0]), lane: (($tail | split("-")) as $p | if ($p | length) >= 3 then $p[1:-1] | join("-") else null end)} end) as $named
    | {run: .databaseId, version: $named.version, lane: $named.lane,
       sha: .headSha[0:8], full: .headSha, at: .createdAt, came: .conclusion}]' 2> /dev/null || echo '[]')
[ -n "$reds" ] || reds='[]'
reds=$(jq -c '.[]' <<< "$reds" | while read -r red; do
  id=$(jq -r .run <<< "$red")
  version=$(jq -r '.version // ""' <<< "$red")
  if [ -z "$version" ]; then
    version=$(gh api "repos/$repo/commits/$(jq -r .full <<< "$red")/statuses" --jq '[.[] | select(.context == "cop")][0].description // ""' 2> /dev/null |
      sed -n 's/^red gate as \([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\):.*/\1/p')
    [ -n "$version" ] || continue
  fi
  failed=$(gh run view "$id" -R "$repo" --json jobs --jq '[.jobs[] | select(.conclusion == "failure" or .conclusion == "timed_out") | .name]' 2> /dev/null || echo '[]')
  jq -c --argjson failed "${failed:-[]}" --arg version "$version" '. + {failed: $failed, version: $version} | del(.full)' <<< "$red"
done | jq -s -c '.')
# #endregion the-red-feed

jq -n --argjson run "$run" --arg pushed "$pushed" --arg lines "$lines" --arg main "${main:0:8}" --arg below "$below" --arg below_pushed "$below_pushed" --arg live "$live" --argjson reds "${reds:-[]}" \
  '{runId: $run, pushedAt: (if $pushed == "" then null else $pushed end), changedLines: (if $lines == "" then null else ($lines | tonumber) end),
    below: {version: (if $below == "" then null else $below end), sha: (if $main == "" then null else $main end), pushedAt: (if $below_pushed == "" then null else $below_pushed end), liveAt: (if $live == "" then null else $live end)}, reds: $reds}' > "$out" || echo '{}' > "$out"
echo "gate feed: run $run, $(jq '.reds | length' "$out" 2> /dev/null || echo 0) red gate run(s) since the last promote, $(jq -r '.changedLines // "unknown"' "$out") lines changed"
exit 0
