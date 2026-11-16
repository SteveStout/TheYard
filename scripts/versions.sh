#!/usr/bin/env bash
# versions.sh
#
# Does:      Two things about version numbers. Sourced, it gives promote.sh and preflight.sh the one reading of
#            docs/CHANGELOG.md's version lines (`versions`) and the number after a version (`next_version`). Run, it
#            is the claim check a lane runs before it pushes gate/<version> (ADR: The version comes from the
#            changelog, the addendum on the claim check): the changelog's top line is filled, it is exactly one above
#            origin/main's top line in its fourth number (or the owner's move of the third, named in OWNER_YES, as
#            promote.sh allows), and no gate/<that version> branch sits on origin at any commit but this one. Each
#            refusal is one plain sentence and the next free version; exits non-zero on a refusal.
# Does not:  Delete, move or force-push any branch, and does not judge a leftover gate branch below main: those never
#            block, because only gate/<top line> is ever pushed, and it names them for whoever rules on them. Read a
#            token. Replace promote.sh's own version checks, which stay as the backstop after the gate.
# Used by:   scripts/preflight.sh (run), scripts/promote.sh (sourced), and whoever ships, before CLAIM.

# The changelog's version lines, newest first, as four numbers each.
versions() { sed -n 's/^- \*\*\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\)\*\*.*/\1/p'; }

# The version after $1 in its fourth number.
next_version() { echo "${1%.*}.$((${1##*.} + 1))"; }

# #region the-claim
claim_refuse() {
  echo "version claim REFUSED: $*"
  exit 1
}

claim_check() {
  cd "$(git rev-parse --show-toplevel)" || claim_refuse "this is not a git checkout"
  local first top main next owner_move ref code sha head when subject
  # The top entry of the changelog, filled or not: an @VER@ line is a version nobody has numbered yet.
  first=$(grep -m1 -E '^- \*\*[^*]+\*\*' docs/CHANGELOG.md | sed 's/^- \*\*\([^*]*\)\*\*.*/\1/')
  [ "$first" != "@VER@" ] || claim_refuse "the changelog's top line is still @VER@: fill the version first, as main's top line plus one at the moment you claim"
  top=$(versions < docs/CHANGELOG.md | sed -n 1p)
  [ -n "$top" ] && [ "$first" = "$top" ] || claim_refuse "the changelog's top line '$first' is not a version of four numbers"

  git fetch -q origin +refs/heads/main:refs/remotes/origin/main 2> /dev/null || claim_refuse "origin could not be read, so nobody can say whether $top is free"
  main=$(git show origin/main:docs/CHANGELOG.md | versions | sed -n 1p)
  [ -n "$main" ] || claim_refuse "origin/main's changelog names no version"
  next=$(next_version "$main")

  if [ "$top" != "$next" ]; then
    # The owner's path: the third number moves by one and the fourth starts at 0, named by the person who promotes.
    IFS=. read -r a b c _ <<< "$main"
    owner_move="$a.$b.$((c + 1)).0"
    [ "$top" = "$owner_move" ] && [ "${OWNER_YES:-}" = "$top" ] ||
      claim_refuse "the changelog says $top and main is on $main: a version is main's top line plus one in its fourth number, so the next free version is $next"
  fi

  ref="refs/heads/gate/$top"
  sha=$(git ls-remote --exit-code origin "$ref")
  code=$?
  head=$(git rev-parse HEAD)
  if [ "$code" -eq 2 ]; then
    echo "version claim ok: $top is main's $main plus one and no gate/$top is on origin"
  elif [ "$code" -ne 0 ]; then
    claim_refuse "origin's branches could not be read (git ls-remote exit $code), so nobody can say whether gate/$top is free"
  elif [ "${sha%%[[:space:]]*}" = "$head" ]; then
    echo "version claim ok: gate/$top is on origin at this commit, ${head:0:8}: your own branch"
  else
    sha=${sha%%[[:space:]]*}
    when="unknown age"
    subject="its commit could not be fetched"
    if git fetch -q origin "$ref" 2> /dev/null; then
      when=$(git log -1 --format='%cr, %ci' FETCH_HEAD)
      subject=$(git log -1 --format='%s' FETCH_HEAD)
    fi
    claim_refuse "gate/$top is already on origin at ${sha:0:8} ($when): \"$subject\". That number is taken whatever its age. Pushing over it needs a force-push with lease (--force-with-lease=gate/$top:${sha:0:8}), which is Steve's to run or allow; until he does, no lane can ship $top"
  fi

  # Leftover gate branches at or below main never block: only gate/<top line> is ever pushed. Named, not judged.
  local left=""
  while read -r v; do
    if [ "$(printf '%s\n%s\n' "$v" "$main" | sort -V | tail -1)" = "$main" ]; then left="$left $v"; fi
  done < <(git ls-remote origin 'refs/heads/gate/*' 2> /dev/null | sed 's#.*refs/heads/gate/##' | sort -V)
  if [ -n "$left" ]; then echo "  left on origin at or below main, not blocking:$left"; fi
}
# #endregion the-claim

if [ "${BASH_SOURCE[0]}" = "$0" ]; then
  set -uo pipefail
  claim_check
fi
