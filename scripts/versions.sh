#!/usr/bin/env bash
# versions.sh [next | gate <branch>]
#
# Does:      Everything about version numbers that more than one script reads. Sourced, it gives promote.sh,
#            gate_pr.sh, fill_version.sh, gate_lock.sh and preflight.sh the one reading of docs/CHANGELOG.md's version
#            lines (`versions`), the number after a version (`next_version`) and the version a gate branch names
#            (`branch_version`: the text after gate/ up to the first hyphen, so gate/<version>-<lane>-<sha8> and the
#            older gate/<version> both read). Run bare, it is the claim check a lane runs under the gate lock before
#            it pushes (ADR-031, the addendums on the claim check and on the fill at the claim): the changelog's top
#            line is filled, it is exactly one above origin/main's top line in its fourth number (or the owner's move
#            of the third, named in OWNER_YES, as promote.sh allows), and the held gate lock does not name it for
#            another lane. Taken means on main or named in the held lock, never "a branch exists". Each refusal is
#            one plain sentence and the next free version; exits non-zero on a refusal. `next` prints origin/main's
#            top line plus one, the number a lane claims the lock for. `gate <branch>` is the gate's first check: the
#            branch's version is the changelog's top line and still origin/main's top line plus one.
# Does not:  Delete, move or force-push any branch, write the lock (scripts/gate_lock.sh does), or judge a gate
#            branch on origin: those never block, and the ones it sees are named for whoever rules on them. Read a
#            token. Replace promote.sh's own version checks, which stay as the backstop after the gate.
# Used by:   scripts/preflight.sh (run), scripts/promote.sh, scripts/gate_pr.sh, scripts/fill_version.sh and
#            scripts/gate_lock.sh (sourced), .github/workflows/gate.yml's number job (`gate`), and whoever ships.

# The changelog's version lines, newest first, as four numbers each.
versions() { sed -n 's/^- \*\*\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\)\*\*.*/\1/p'; }

# The version after $1 in its fourth number.
next_version() { echo "${1%.*}.$((${1##*.} + 1))"; }

# The version a gate branch names: the text after gate/ up to the first hyphen. A name with no hyphen is the older
# gate/<version>, and its whole tail is the version.
branch_version() {
  local tail=${1#refs/heads/}
  tail=${tail#gate/}
  echo "${tail%%-*}"
}

# The gate lock every lane takes before it claims (scripts/gate_lock.sh writes it). Read here, never written.
gate_lock_path() { echo "${GATE_LOCK:-/mnt/j/dispatch/locks/theyard-gate.lock}"; }

# One field of a lock line, `key=value` among words separated by spaces, as dispatch reads it.
lock_field() { tr ' ' '\n' <<< "$1" | sed -n "s/^$2=//p" | head -n 1; }

# #region the-claim
claim_refuse() {
  echo "version claim REFUSED: $*"
  exit 1
}

claim_check() {
  cd "$(git rev-parse --show-toplevel)" || claim_refuse "this is not a git checkout"
  local first top main next owner_move lock line holder named since
  # The top entry of the changelog, filled or not: the placeholder the fill replaces is a version nobody has numbered
  # yet. It is spelled in two parts, because the fill replaces every whole one in the tree, this file's included.
  local placeholder="@""VER@"
  first=$(grep -m1 -E '^- \*\*[^*]+\*\*' docs/CHANGELOG.md | sed 's/^- \*\*\([^*]*\)\*\*.*/\1/')
  [ "$first" != "$placeholder" ] || claim_refuse "the changelog's top line is still the placeholder $placeholder: fill the version first, with scripts/fill_version.sh once you hold the gate lock"
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

  # The held lock is the only other place a number is taken. Absent, the number is free by the lock; a folder that
  # cannot be reached (another machine, the share not mounted) is said and not judged, and promote stays the backstop.
  lock=$(gate_lock_path)
  if [ ! -d "$(dirname "$lock")" ]; then
    echo "version claim ok: $top is main's $main plus one; the gate lock at $lock could not be reached, so it is not judged"
  elif [ ! -e "$lock" ]; then
    echo "version claim ok: $top is main's $main plus one and no gate lock is held"
  elif ! line=$(head -n 1 "$lock" 2> /dev/null); then
    echo "version claim ok: $top is main's $main plus one; the gate lock at $lock could not be read, so it is not judged"
  else
    holder=$(lock_field "$line" lane)
    named=$(lock_field "$line" version)
    since=$(lock_field "$line" since)
    if [ -n "${GATE_LANE:-}" ] && [ "$holder" = "$GATE_LANE" ]; then
      [ "$named" = "$top" ] || claim_refuse "the gate lock you hold names $named and this commit claims $top: release the lock and claim again for $next"
      echo "version claim ok: $top is main's $main plus one and the gate lock is yours, $holder"
    elif [ "$named" = "$top" ]; then
      claim_refuse "$top is named in the gate lock held by ${holder:-a lane that wrote no name} since ${since:-an unknown time}; wait for that lock to go and claim main's top line plus one then (if that lock is yours, set GATE_LANE to your lane)"
    else
      echo "version claim ok: $top is main's $main plus one; the gate lock is held by ${holder:-a lane that wrote no name} for ${named:-no version}, not this one"
    fi
  fi

  # Gate branches never take a number: a red gate's branch is left for whoever rules on it, and frees its number with
  # its lock. The ones on origin are named, not judged.
  local below="" above="" tail v
  while read -r tail; do
    v=$(branch_version "$tail")
    if [ "$(printf '%s\n%s\n' "$v" "$main" | sort -V | tail -1)" = "$main" ]; then below="$below $tail"; else above="$above $tail"; fi
  done < <(git ls-remote origin 'refs/heads/gate/*' 2> /dev/null | sed 's#.*refs/heads/gate/##' | sort -V)
  if [ -n "$below" ]; then echo "  left on origin at or below main, not blocking:$below"; fi
  if [ -n "$above" ]; then echo "  on origin above main, not blocking (a branch never takes a number):$above"; fi
}
# #endregion the-claim

# #region the-gate-check
# The gate's first check, run the moment the run holds the gate's group: a second lane that claimed the same number is
# turned away in seconds instead of after a whole gate. Prints a GitHub annotation, because only the gate runs it.
gate_check() {
  local branch=$1 v top main next a b c
  v=$(branch_version "$branch")
  [[ "$v" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || {
    echo "::error::the branch $branch names no version of four numbers after gate/"
    exit 1
  }
  top=$(versions < docs/CHANGELOG.md | sed -n 1p)
  [ "$top" = "$v" ] || {
    echo "::error::the branch names $v and the changelog's top line is ${top:-not a version}"
    exit 1
  }
  if [ "$(git rev-parse --is-shallow-repository)" = true ]; then
    git fetch -q --depth=1 origin +refs/heads/main:refs/remotes/origin/main
  else
    git fetch -q origin +refs/heads/main:refs/remotes/origin/main
  fi || {
    echo "::error::origin's main could not be read, so nobody can say whether $v is still free"
    exit 1
  }
  main=$(git show origin/main:docs/CHANGELOG.md | versions | sed -n 1p)
  next=$(next_version "$main")
  IFS=. read -r a b c _ <<< "$main"
  # The owner's move passes here: promote ends it green with its notice and the owner promotes it by hand.
  if [ "$v" != "$next" ] && [ "$v" != "$a.$b.$((c + 1)).0" ]; then
    echo "::error::$v is no longer main's top line plus one: main is on $main, so this is a second claim of a number already shipped or skipped. Release the lock and claim $next"
    exit 1
  fi
  echo "number ok: $v is main's $main plus one"
}
# #endregion the-gate-check

if [ "${BASH_SOURCE[0]}" = "$0" ]; then
  set -uo pipefail
  case "${1:-}" in
    "") claim_check ;;
    next)
      git fetch -q origin +refs/heads/main:refs/remotes/origin/main 2> /dev/null || {
        echo "origin could not be read" >&2
        exit 1
      }
      next_version "$(git show origin/main:docs/CHANGELOG.md | versions | sed -n 1p)"
      ;;
    gate) gate_check "${2:?usage: versions.sh gate <branch>}" ;;
    *)
      echo "usage: versions.sh [next | gate <branch>]" >&2
      exit 2
      ;;
  esac
fi
