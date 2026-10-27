#!/usr/bin/env bash
# promote.sh <raw> <gate-seconds> [<version>] [<run-id>]
#
# Does:      Promotes one green gate run onto main (ADR: The gate runs where the memory is, the addendum of
#            1.0.4.101): checks that the commit is the one the run tested and sits one commit on main, that the
#            version goes up by one in its fourth number, the locked resume, and the Cloud roles; writes
#            data/test-results.json and the README's figures from the run's reports in <raw>, folds them into the
#            commit, and fast-forwards main to it. Never force-pushes. Writes sha, version and functions to
#            $GITHUB_OUTPUT when it runs in the gate.
# Does not:  Run a check (the gate did), roll the site (the gate calls Deploy and Deploy Render), or push a commit
#            that changes a workflow from the gate (the token may not; a person runs this by hand for that one).
# Used by:   .github/workflows/gate.yml's promote job, and a person promoting a run by hand, naming the run:
#            gh run download <run> -n gate-raw -D raw && scripts/promote.sh raw <seconds> <version> <run>
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
green=$(gh run view "$run" --json jobs --jq '[.jobs[] | select(.name == "gate" or .name == "shed") | select(.conclusion == "success")] | length') || refuse "gate run $run's jobs could not be read"
[ "$green" = 2 ] || refuse "gate run $run's gate and shed jobs are not both green"
git fetch -q origin +refs/heads/main:refs/remotes/origin/main
[ "$(git rev-parse HEAD~1 2>/dev/null)" = "$(git rev-parse origin/main)" ] || refuse "the gate branch is not one commit on main"
# #endregion the-tested-commit

# #region the-version
# The branch names the changelog's top line, the line moved up by exactly one in its fourth number (the first three
# are the owner's), and it sits directly on the version main holds.
top=$(versions < docs/CHANGELOG.md | sed -n 1p)
below=$(versions < docs/CHANGELOG.md | sed -n 2p)
[ "$top" = "$version" ] || refuse "the branch names $version and the changelog's top line is $top"
[ "$top" != "$below" ] || refuse "the changelog's top line is still $below: a version ships with its own line"
[ "${top%.*}" = "${below%.*}" ] || refuse "the version moved past its fourth number, $below to $top: the first three numbers are Steve's, and that version ships from a machine with his yes"
[ "$(printf '%s\n%s\n' "$below" "$top" | sort -V | tail -1)" = "$top" ] || refuse "the version goes down, $below to $top"
[ "${top##*.}" -eq $((${below##*.} + 1)) ] || refuse "the version skips, $below to $top: the fourth number moves by one"
git fetch -q origin +refs/heads/main:refs/remotes/origin/main
on_main=$(git show origin/main:docs/CHANGELOG.md | versions | sed -n 1p)
[ "$on_main" = "$below" ] || refuse "main is on $on_main and $version sits on $below"
echo "version $below to $version, the fourth number only"
# #endregion the-version

# The resume the site serves is the one locked on 24 October (RESUME-LOCK-2026-10-24.md).
echo "c00ee5e0bdb019e2d531edfdec242e9229ca54b953bd83f39bdefe8dca7e4d13  api/TheYard.Api/wwwroot/docs/resume.pdf" | sha256sum -c --quiet ||
  refuse "the served resume is not the locked one"

# #region cloud-roles
# A live Cloud card that says the site's identity is missing a role stops the ship before anything is written.
# A card that does not answer is said, never failed on: the ship may be its fix. "not read: no hour kept yet"
# is an honest wait, so only the three sentences of a missing role count.
missing=""
for site in https://theyard.stevenstout.biz https://theyard-cosmos.stevenstout.biz; do
  for card in sqldb cosmosdb registry insights mail edge nightly; do
    if ! text=$(curl -fsS --max-time 30 -A 'TheYard-SelfRead/1 (promote)' "$site/api/admin/cloud/$card?window=24h"); then
      echo "cloud roles: $site $card did not answer; not judged"
      continue
    fi
    said=$(grep -oE '(no role on|not read: no role|holds no role)[^"]{0,120}' <<< "$text" | head -n 1 || true)
    [ -z "$said" ] || missing="$missing $site/$card: $said;"
  done
done
[ -z "$missing" ] || refuse "a live Cloud card says the site's identity is missing a role:$missing grant Reader on the scope it names"
echo "cloud roles: every card on both names reads with the roles it needs"
# #endregion cloud-roles

# #region the-ancestor
git merge-base --is-ancestor origin/main HEAD || refuse "main is not an ancestor of the gate's commit: rebase onto main and gate again"
if [ -n "${GITHUB_ACTIONS:-}" ] && git diff --name-only origin/main HEAD -- .github/workflows | grep -q .; then
  refuse "this version changes a workflow, which the gate's token may not push: promote it by hand with scripts/promote.sh"
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
python3 scripts/readme_results.py
extra=$(git status --porcelain -uall --no-renames | cut -c4- | grep -vxE 'data/test-results\.json|README\.md|docs/built-with-ai/BUILT-WITH-AI\.md|docs/images/og\.svg|public/og\.png' || true)
[ -z "$extra" ] || refuse "the results touched more than their three files and the version card: $extra"
git add data/test-results.json README.md docs/built-with-ai/BUILT-WITH-AI.md docs/images/og.svg public/og.png
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
  { echo "sha=$sha"; echo "version=$version"; echo "functions=$functions"; } >> "$GITHUB_OUTPUT"
fi
