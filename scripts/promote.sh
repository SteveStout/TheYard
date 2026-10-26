#!/usr/bin/env bash
# promote.sh <raw> <gate-seconds> [<version>]
#
# Does:      Promotes one green gate run onto main (ADR: The gate runs where the memory is, the addendum of
#            1.0.4.101): checks the version, the locked resume and that main is an ancestor of the gate's commit,
#            writes data/test-results.json and the README's figures from the run's reports in <raw>, folds them into
#            the commit, and fast-forwards main to it. Never force-pushes. Writes sha, version and rolls to
#            $GITHUB_OUTPUT when it runs in the gate.
# Does not:  Run a check (the gate did), roll the site (the gate calls Deploy and Deploy Render), or push a commit
#            that changes a workflow from the gate (the token may not; a person runs this by hand for that one).
# Used by:   .github/workflows/gate.yml's promote job, and a person promoting a run by hand:
#            gh run download <run> -n gate-raw -D raw && scripts/promote.sh raw <seconds> <version>
set -euo pipefail
raw=$1
seconds=$2
version=${3:-${GITHUB_REF_NAME#gate/}}
cd "$(git rev-parse --show-toplevel)"
refuse() {
  echo "::error::$*"
  exit 1
}
versions() { sed -n 's/^- \*\*\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\)\*\*.*/\1/p'; }

# #region the-version
# The branch names the changelog's top line, the line moved, only its fourth number moved (the first three are
# Steve's), and it sits directly on the version main holds.
top=$(versions < docs/CHANGELOG.md | sed -n 1p)
below=$(versions < docs/CHANGELOG.md | sed -n 2p)
[ "$top" = "$version" ] || refuse "the branch names $version and the changelog's top line is $top"
[ "$top" != "$below" ] || refuse "the changelog's top line is still $below: a version ships with its own line"
[ "${top%.*}" = "${below%.*}" ] || refuse "the version moved past its fourth number, $below to $top: the first three numbers are Steve's, and that version ships from a machine with his yes"
git fetch -q origin +refs/heads/main:refs/remotes/origin/main
on_main=$(git show origin/main:docs/CHANGELOG.md | versions | sed -n 1p)
[ "$on_main" = "$below" ] || refuse "main is on $on_main and $version sits on $below"
echo "version $below to $version, the fourth number only"
# #endregion the-version

# The resume the site serves is the one Steve locked (RESUME-LOCK-2026-10-24.md).
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
node scripts/test_results.mjs "$raw" "$version" "$seconds"
python3 scripts/readme_results.py
extra=$(git status --porcelain -uall --no-renames | cut -c4- | grep -vxE 'data/test-results\.json|README\.md|docs/built-with-ai/BUILT-WITH-AI\.md' || true)
[ -z "$extra" ] || refuse "the results touched more than their three files: $extra"
git add data/test-results.json README.md docs/built-with-ai/BUILT-WITH-AI.md
GIT_COMMITTER_NAME="$(git log -1 --format=%an)" GIT_COMMITTER_EMAIL="$(git log -1 --format=%ae)" git commit -q --amend --no-edit
sha=$(git rev-parse HEAD)
echo "the results folded into the commit: ${sha:0:8}"
# #endregion the-results

# A version that changes only the job hunt's files rolls nothing, as Deploy's path filter says.
rolls=false
if git diff --name-only origin/main HEAD | grep -qvE '^(functions/|infra/jobhunt\.bicep$|\.github/workflows/nightly\.yml$)'; then rolls=true; fi

# #region fast-forward
for try in 1 2 3 4; do
  git fetch -q origin +refs/heads/main:refs/remotes/origin/main
  git merge-base --is-ancestor origin/main HEAD || refuse "main moved past the gate's commit: rebase onto main and gate again"
  if git push -q origin "HEAD:refs/heads/main"; then break; fi
  [ "$try" -lt 4 ] && sleep $((try * 30))
done
[ "$(git ls-remote origin refs/heads/main | cut -f1)" = "$sha" ] || refuse "main was not fast-forwarded to ${sha:0:8} in four tries"
echo "main is now ${sha:0:8}, $version; rolls: $rolls"
# #endregion fast-forward

if [ -n "${GITHUB_OUTPUT:-}" ]; then
  { echo "sha=$sha"; echo "version=$version"; echo "rolls=$rolls"; } >> "$GITHUB_OUTPUT"
fi
