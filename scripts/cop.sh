#!/usr/bin/env bash
# cop.sh <branch> <out-dir>
#
# Does:      The traffic cop's first step, run by the Gate run that holds the gate's group (ADR-137, One traffic
#            cop for main). On a pushed ready/<lane>-<sha8>, one commit with the placeholders unfilled: lifts the
#            lane's changelog line out, gives the changelog, the quality ratchet and the version card main's side,
#            rebases the rest onto origin/main, puts the line back on top of main's and fills the version, the date
#            and the record number with scripts/fill_version.sh, so the number is main's top line plus one at the
#            moment this run holds the gate. A branch that cannot be filled (two commits, a top line that is not the
#            placeholder, a gate.yml older than main's, a rebase conflict outside the changelog) is skipped GREEN with
#            the reason: nothing was tested, so nothing is red (ADR-098). On gate/<version>-<lane>-<sha8> it takes the
#            old path, versions.sh's gate check, until origin/main's gate.yml carries the ready/** trigger, and
#            refuses it red after. Either way the commit every later job tests leaves as <out-dir>/filled.bundle and
#            <out-dir>/filled.sha, and landed, reason, version, sha, ready, base and workflow go to $GITHUB_OUTPUT.
# Does not:  Push anything, post a commit status (the workflow does, from these outputs), run a check, take the gate
#            lock, or retry: a skipped lane rebases and pushes a new ready branch itself. Read, print or pass a token
#            value: HAS_PROMOTE_TOKEN says only whether the secret exists.
# Used by:   .github/workflows/gate.yml's cop job, on a checkout of the pushed commit with the whole history.
set -euo pipefail
branch=${1:?usage: cop.sh <branch> <out-dir>}
dir=${2:?usage: cop.sh <branch> <out-dir>}
cd "$(git rev-parse --show-toplevel)"
# shellcheck source=scripts/versions.sh
. scripts/versions.sh
out=${GITHUB_OUTPUT:-/dev/null}
said() { printf '%s=%s\n' "$1" "$2" | tee -a "$out"; }
refuse() {
  echo "::error::$*"
  exit 1
}
# A skip tested nothing, so the run ends green; the workflow posts the reason as a failing status on the branch's commit.
skip() {
  echo "::notice::the cop skipped $branch: $*"
  said landed skip
  said reason "$*"
  exit 0
}
# The placeholder, spelled in two parts, so the fill never rewrites this file.
ver_mark="@""VER@"
ready=$(git rev-parse HEAD)
said ready "$ready"
git fetch -q origin +refs/heads/main:refs/remotes/origin/main || refuse "origin's main could not be read"
main=$(git rev-parse origin/main)
said base "$main"
# Main has the cop once its gate.yml runs on ready branches.
cop_on_main() { git show origin/main:.github/workflows/gate.yml 2> /dev/null | grep -qF 'ready/**'; }

case "$branch" in
  # #region the-old-path
  gate/*)
    if cop_on_main; then
      refuse "gate/* is closed: main runs the traffic cop. Leave the numbers as placeholders, rebase onto main and push ready/<lane>-<sha8> (CLAUDE.md, Shipping)"
    fi
    bash scripts/versions.sh gate "$branch" || exit 1
    version=$(branch_version "$branch")
    ;;
  # #endregion the-old-path

  # #region the-lift
  ready/*)
    from=$(git merge-base origin/main HEAD)
    count=$(git rev-list --count "$from..HEAD")
    [ "$count" = 1 ] || skip "the branch is $count commits off main and a ready branch is one: squash it, rebase onto main and push a new ready branch"
    # A branch whose gate.yml is not main's ran an old gate: rebase it, unless changing gate.yml is the commit's own work.
    if ! git diff --quiet origin/main HEAD -- .github/workflows/gate.yml && git diff --quiet "$from" HEAD -- .github/workflows/gate.yml; then
      skip "the branch carries an older gate.yml than main's: rebase onto main ${main:0:8} and push a new ready branch"
    fi
    entry=$(grep -m1 -E '^- \*\*' docs/CHANGELOG.md || true)
    case "$entry" in
      "- **$ver_mark**"*) ;;
      *) skip "the changelog's top line is not the version placeholder: a lane never picks a version, the cop fills it at landing" ;;
    esac
    # The changelog changes by its one line and nothing else; anything more would be lost in the lift.
    if ! diff -q <(grep -vxF -- "$entry" docs/CHANGELOG.md) <(git show "$from:docs/CHANGELOG.md") > /dev/null; then
      skip "the changelog changes beyond its one new line: keep the rest of it as main has it"
    fi
    # Main's side of the files every lane touches at the same place: the changelog (the line goes back below), the
    # ratchet and the card, which the fill tightens and redraws.
    for path in docs/CHANGELOG.md .github/quality-ratchet.json docs/images/og.svg public/og.png; do
      if git cat-file -e "$from:$path" 2> /dev/null; then git checkout -q "$from" -- "$path"; fi
    done
    git commit -q --amend --allow-empty --no-edit
    if ! git rebase -q origin/main > /dev/null 2>&1; then
      files=$(git diff --name-only --diff-filter=U | tr '\n' ' ')
      git rebase --abort
      skip "the rebase onto main ${main:0:8} conflicts in ${files% }: that change is the lane's to rebase"
    fi
    # The line goes back on top of main's newest.
    log=$(mktemp)
    awk -v line="$entry" '!done && /^- \*\*/ { print line; done = 1 } { print }' docs/CHANGELOG.md > "$log"
    cat "$log" > docs/CHANGELOG.md
    rm -f "$log"
    git add docs/CHANGELOG.md
    git commit -q --amend --no-edit
    if ! filled=$(bash scripts/fill_version.sh 2>&1); then
      skip "${filled##*fill REFUSED: }"
    fi
    echo "$filled"
    version=$(sed -n 's/^VERSION=\([^ ]*\).*/\1/p' <<< "$filled")
    ;;
  # #endregion the-lift
  *) refuse "the gate runs on ready/<lane>-<sha8>, not on $branch" ;;
esac

# #region the-hand-on
# One way for every version: a bundle of the commit on top of main, which any later job (and a person promoting by
# hand) fetches onto a checkout of main. A push by the run's own token cannot carry a workflow change, so an internal
# branch would not work for every version; the bundle needs no write at all.
sha=$(git rev-parse HEAD)
mkdir -p "$dir"
git branch -f cop-filled "$sha"
git bundle create -q "$dir/filled.bundle" cop-filled "^$(git merge-base origin/main HEAD)"
echo "$sha" > "$dir/filled.sha"
workflow=false
if git diff --name-only origin/main HEAD -- .github/workflows | grep -q .; then workflow=true; fi
if [ "$workflow" = true ] && [ "${HAS_PROMOTE_TOKEN:-}" != true ]; then
  echo "::notice::this version changes a workflow and PROMOTE_TOKEN is missing: the gate runs, and a green run ends needing a hand promote (CLAUDE.md, Shipping)"
fi
said landed go
said version "$version"
said sha "$sha"
said workflow "$workflow"
# #endregion the-hand-on
