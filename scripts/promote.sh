#!/usr/bin/env bash
# promote.sh <raw> <gate-seconds> [<version>] [<run-id>]
#
# Does:      Promotes one green gate run onto main (ADR: The gate runs where the memory is, the addendum of
#            1.0.4.101): checks that the commit is the one the run tested and sits one commit on main, that the
#            version goes up by one in its fourth number, the locked resume, and the Cloud roles; writes
#            data/test-results.json and the README's figures from the run's reports in <raw>, adds CodeRabbit's row
#            to data/coderabbit-findings.json and this version's row to data/quality-history.json, folds them into the
#            commit, and fast-forwards main to it. A version that moves the first three numbers is promoted only by
#            hand, with OWNER_YES naming it. Never force-pushes. Writes sha, version and functions to
#            $GITHUB_OUTPUT when it runs in the gate.
# Does not:  Run a check (the gate did), roll the site (the gate calls Deploy and Deploy Render, or the push does
#            when a personal token made it), or read, print or pass a token value: a version that changes a
#            workflow is carried by the secret PROMOTE_TOKEN the promote job checks out with, and where that
#            secret does not exist this ends green with a notice and moves nothing.
# Used by:   .github/workflows/gate.yml's promote job, and a person promoting a run by hand, naming the run:
#            raw=$(mktemp -d) && gh run download <run> -n gate-raw -D "$raw" && { gh run download <run> -n coderabbit-raw -D "$raw" || rm -f "$raw/coderabbit.json"; } && scripts/promote.sh "$raw" <seconds> <version> <run>
#            A fresh folder outside the worktree each time: a reused one can hold another run's coderabbit.json, which
#            this would fold as this version's row, and a folder inside the worktree is a file the results check refuses.
set -euo pipefail
raw=$1
seconds=$2
version=${3:-${GITHUB_REF_NAME#gate/}}
run=${4:-${GITHUB_RUN_ID:-}}
cd "$(git rev-parse --show-toplevel)"
refuse() {
  echo "::error::$*"
  exit 1
}
versions() { sed -n 's/^- \*\*\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\)\*\*.*/\1/p'; }

# #region the-tested-commit
# Before anything is written: HEAD is the commit the named run tested, and it is one commit on main, so the gate's
# two cuts (the diff of that one commit) saw everything this push ships.
[ -n "$run" ] || refuse "name the gate run this promotes: scripts/promote.sh <raw> <seconds> <version> <run>"
tested=$(gh run view "$run" --json headSha --jq .headSha) || refuse "gate run $run could not be read"
[ "$tested" = "$(git rev-parse HEAD)" ] || refuse "gate run $run tested ${tested:0:8} and HEAD is $(git rev-parse --short=8 HEAD)"
# The jobs it trusts, by name: the gate's verdict, The Shed, and the check jobs the verdict folds, which run side by
# side since 1.0.5.4 (ADR-098, the addendum of that version). Every one green, or nothing is promoted.
checks='["gate","shed","dotnet","cosmos","node","browser 1 of 2","browser 2 of 2"]'
green=$(gh run view "$run" --json jobs --jq "[.jobs[] | select(.name as \$n | $checks | index(\$n)) | select(.conclusion == \"success\")] | length") || refuse "gate run $run's jobs could not be read"
[ "$green" = 7 ] || refuse "gate run $run's check jobs are not all green: $green of the 7 it trusts"
# The CodeRabbit job ends green at Level 0 whatever the review found; a run that has the job and did not finish it
# green has not read the review yet (ADR-129). A run from before the job existed has none to wait for.
review=$(gh run view "$run" --json jobs --jq '[.jobs[] | select(.name == "coderabbit")] | if length == 0 then "none" else .[0].conclusion end') || refuse "gate run $run's jobs could not be read"
[ "$review" = none ] || [ "$review" = success ] || refuse "gate run $run's coderabbit job is $review, not green"
# A Critical finding on a review that was read holds the promote (ADR-129, the addendum of 1.0.5.21). A review that never
# came, a row whose findings were not read, or no row at all ships with its notice, as at Level 0.
if [ -f "$raw/coderabbit.json" ]; then
  said=$(jq -r '.reviewed // ""' "$raw/coderabbit.json") || refuse "the run's review row could not be read"
  critical=$(jq -r '.findings.critical // 0' "$raw/coderabbit.json") || refuse "the run's review row could not be read"
  case "$said" in
    "not reviewed"* | *"not read") ;;
    *)
      if [ "$critical" -gt 0 ]; then
        where=$(jq -r '[.critical_at[]? | (if .line then "\(.path):\(.line)" else .path end) + (if .pr then " on #\(.pr)" else "" end)] | join(", ")' "$raw/coderabbit.json")
        refuse "a Critical finding holds the promote: CodeRabbit marked $critical Critical (${where:-in the review body}); fix it on this gate branch, or leave the call to the owner"
      fi
      ;;
  esac
fi
git fetch -q origin +refs/heads/main:refs/remotes/origin/main
[ "$(git rev-parse HEAD~1 2>/dev/null)" = "$(git rev-parse origin/main)" ] || refuse "the gate branch is not one commit on main"
# #endregion the-tested-commit

# #region the-version
# The branch names the changelog's top line, the line moved up by exactly one in its fourth number (the first three
# are the owner's, below), and it sits directly on the version main holds.
top=$(versions < docs/CHANGELOG.md | sed -n 1p)
below=$(versions < docs/CHANGELOG.md | sed -n 2p)
[ "$top" = "$version" ] || refuse "the branch names $version and the changelog's top line is $top"
[ "$top" != "$below" ] || refuse "the changelog's top line is still $below: a version ships with its own line"
[ "$(printf '%s\n%s\n' "$below" "$top" | sort -V | tail -1)" = "$top" ] || refuse "the version goes down, $below to $top"
if [ "${top%.*}" = "${below%.*}" ]; then
  [ "${top##*.}" -eq $((${below##*.} + 1)) ] || refuse "the version skips, $below to $top: the fourth number moves by one"
  moved="the fourth number only"
else
  # The first three numbers are the owner's (ADR-130). A move in them ships only by hand, from a machine where the
  # person promoting names the version as OWNER_YES; the owner's yes itself is written in the version's record by
  # whoever makes it, and this script cannot read it, so it checks only that the person named the version. It goes
  # up by one in its third number and starts the fourth again at 0. The gate never takes one on its own, whatever
  # its environment says: it ends green with a notice, as it does for a version that changes a workflow, so green
  # code sends no failure email.
  IFS=. read -r a b c _ <<< "$below"
  [ "$top" = "$a.$b.$((c + 1)).0" ] || refuse "the version moved past its fourth number, $below to $top, and not to $a.$b.$((c + 1)).0: the first three numbers are Steve's, and the third moves by one"
  if [ -n "${GITHUB_ACTIONS:-}" ] || [ "${OWNER_YES:-}" != "$top" ]; then
    if [ -n "${GITHUB_ACTIONS:-}" ]; then
      echo "::notice::$top moves the first three numbers: promote it by hand with OWNER_YES=$top scripts/promote.sh"
      if [ -n "${GITHUB_OUTPUT:-}" ]; then { echo "promoted=false"; echo "by_push=false"; } >> "$GITHUB_OUTPUT"; fi
      exit 0
    fi
    refuse "the version moved past its fourth number, $below to $top: the first three numbers are Steve's, and that version ships from a machine with his yes (OWNER_YES=$top)"
  fi
  moved="the first three numbers, with the owner's yes"
fi
git fetch -q origin +refs/heads/main:refs/remotes/origin/main
on_main=$(git show origin/main:docs/CHANGELOG.md | versions | sed -n 1p)
[ "$on_main" = "$below" ] || refuse "main is on $on_main and $version sits on $below"
echo "version $below to $version, $moved"
# #endregion the-version

# The resume the site serves is the one locked on 10 November (RESUME-LOCK-2026-11-10.md).
echo "a1bf893cdfa93c8bf1aa7a678d3d852058f827ff475bf45d3ee630a9d399c05a  api/TheYard.Api/wwwroot/docs/resume.pdf" | sha256sum -c --quiet ||
  refuse "the served resume is not the locked one"

# #region cloud-roles
# A live Cloud card that says the site's identity is missing a role stops the ship before anything is written.
# A card that does not answer is said, never failed on: the ship may be its fix. A card that answers with anything
# but JSON (a page in front of the API, a sign-in) is refused, because it could not say a role is missing. "not
# read: no hour kept yet" is an honest wait, so only the three sentences of a missing role count.
missing=""
for site in https://theyard.stevenstout.biz https://theyard-cosmos.stevenstout.biz; do
  for card in sqldb cosmosdb registry insights mail edge nightly; do
    if ! text=$(curl -fsS --max-time 30 -H 'Accept: application/json' -A 'TheYard-SelfRead/1 (promote)' "$site/api/admin/cloud/$card?window=24h"); then
      echo "cloud roles: $site $card did not answer; not judged"
      continue
    fi
    jq -e 'type == "object"' <<< "$text" > /dev/null 2>&1 || refuse "the Cloud card $site/$card answered with something that is not JSON"
    said=$(grep -oE '(no role on|not read: no role|holds no role)[^"]{0,120}' <<< "$text" | head -n 1 || true)
    [ -z "$said" ] || missing="$missing $site/$card: $said;"
  done
done
[ -z "$missing" ] || refuse "a live Cloud card says the site's identity is missing a role:$missing grant Reader on the scope it names"
echo "cloud roles: every card on both names reads with the roles it needs"
# #endregion cloud-roles

# #region the-ancestor
git merge-base --is-ancestor origin/main HEAD || refuse "main is not an ancestor of the gate's commit: rebase onto main and gate again"
# A push that carries a file under .github/workflows needs Workflows write, which the run's own token does not
# have. In the gate that write comes from the secret PROMOTE_TOKEN, which the promote job hands to its checkout,
# so the push at the end of this script already uses it; HAS_PROMOTE_TOKEN says only whether the secret existed,
# and this script never reads, prints or passes the value. Without the secret the job ends GREEN with a notice
# and moves nothing, because green code must not send a failure email (ADR-098, the addendum of 1.0.4.119).
# A push made with a personal token DOES start the push-triggered workflows, unlike the run's own, so when one
# carries this version the roll is the push's and the run must not call the two deploys a second time.
workflow=false
if git diff --name-only origin/main HEAD -- .github/workflows | grep -q .; then workflow=true; fi
by_push=false
if [ "$workflow" = true ]; then
  if [ -z "${GITHUB_ACTIONS:-}" ] || [ "${HAS_PROMOTE_TOKEN:-}" = true ]; then
    by_push=true
    echo "this version changes a workflow and the token that promotes it carries one: the push rolls the site"
  else
    echo "::notice::this version changes a workflow: promote it by hand with scripts/promote.sh"
    if [ -n "${GITHUB_OUTPUT:-}" ]; then { echo "promoted=false"; echo "by_push=false"; } >> "$GITHUB_OUTPUT"; fi
    exit 0
  fi
fi
# #endregion the-ancestor

# #region the-results
if grep -q '^oncosmos=skipped' "$raw/dotnet.result"; then export YARD_CARRY_COSMOS=1; else export YARD_CARRY_COSMOS=0; fi
# The version card the gate drew from this commit, folded in with the results.
if [ -f "$raw/og.svg" ] && [ -f "$raw/og.png" ]; then
  cp "$raw/og.svg" docs/images/og.svg
  cp "$raw/og.png" public/og.png
fi
node scripts/test_results.mjs "$raw" "$version" "$seconds"
# The file the site serves has to describe the run that shipped it. A green gate cannot hold a failing or a
# skipped suite, so a results file that records one was assembled from another attempt's reports: that is what
# happened at 1.0.4.122, where a gate job that went red on one Cosmos test was re-run green and this script
# folded the first attempt's gate-raw into the commit, shipping a record that said a suite failed and turning
# CI on main red on the test that reads it. The reports are the run's, or nothing is promoted.
node -e 'const r = require("./data/test-results.json"); const bad = r.suites.filter(s => s.failed > 0 || s.skipped > 0); if (bad.length) { console.error(bad.map(s => `${s.id}: ${s.failed} failed, ${s.skipped} skipped`).join("; ")); process.exit(1); }'   || refuse "the results file records a suite this green gate did not have: its reports are another attempt's, so re-run the gate by pushing the branch again rather than re-running the job"
python3 scripts/readme_results.py
# CodeRabbit's row for this ship joins the findings log, the evidence every rung of its ladder is decided on (ADR-129).
# The quality history gains this version's row, measured on this tree, the evidence the Quality page draws (ADR-130).
python3 scripts/quality_history.py append "$version" || echo "::warning::the quality history was not measured for $version"
# A run whose read wrote no row still gets one, saying so, so the log never skips a ship.
if [ ! -f "$raw/coderabbit.json" ] && [ "$review" = success ]; then python3 scripts/coderabbit_review.py missing "$version" "$tested" "$raw/coderabbit.json"; fi
if [ -f "$raw/coderabbit.json" ]; then python3 scripts/coderabbit_review.py fold "$raw/coderabbit.json" data/coderabbit-findings.json; fi
extra=$(git status --porcelain -uall --no-renames | cut -c4- | grep -vxE 'data/test-results\.json|data/coderabbit-findings\.json|data/quality-history\.json|README\.md|docs/built-with-ai/BUILT-WITH-AI\.md|docs/images/og\.svg|public/og\.png' || true)
[ -z "$extra" ] || refuse "the results touched more than their five files and the version card: $extra"
git add data/test-results.json README.md docs/built-with-ai/BUILT-WITH-AI.md docs/images/og.svg public/og.png
if [ -f data/coderabbit-findings.json ]; then git add data/coderabbit-findings.json; fi
if [ -f data/quality-history.json ]; then git add data/quality-history.json; fi
GIT_COMMITTER_NAME="$(git log -1 --format=%an)" GIT_COMMITTER_EMAIL="$(git log -1 --format=%ae)" git commit -q --amend --no-edit
sha=$(git rev-parse HEAD)
echo "the results folded into the commit: ${sha:0:8}"
# #endregion the-results

# Every promoted version rolls: the results file and the README are folded into its commit above, and the site
# serves the results file (TestSummary, AboutPage), so even a version of the job hunt's files alone changes what
# the site shows.
# A version that touches the job hunt's Function is deployed to it by a run of its own (deploy-function.yml).
functions=false
if git diff --name-only origin/main HEAD | grep -qE '^functions/'; then functions=true; fi

# #region fast-forward
for try in 1 2 3 4; do
  git fetch -q origin +refs/heads/main:refs/remotes/origin/main
  git merge-base --is-ancestor origin/main HEAD || refuse "main moved past the gate's commit: rebase onto main and gate again"
  if git push -q origin "HEAD:refs/heads/main"; then break; fi
  [ "$try" -lt 4 ] && sleep $((try * 30))
done
[ "$(git ls-remote origin refs/heads/main | cut -f1)" = "$sha" ] || refuse "main was not fast-forwarded to ${sha:0:8} in four tries"
echo "main is now ${sha:0:8}, $version; the Function: $functions"
# #endregion fast-forward

if [ -n "${GITHUB_OUTPUT:-}" ]; then
  { echo "sha=$sha"; echo "version=$version"; echo "functions=$functions"; echo "promoted=true"; echo "by_push=$by_push"; } >> "$GITHUB_OUTPUT"
fi
