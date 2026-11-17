#!/usr/bin/env bash
# gate_lock.sh claim <lane> <version> | annotate <run-id> <branch> | release | show | may_release <lock-line>
#
# Does:      The gate lock a lane holds from its claim to its gate's end (the lock protocol in dispatch/locks/README.md
#            on the share; ADR-031, the addendum on the fill at the claim). `claim` takes flock on a local file for
#            the seconds it reads the lock, creates it with noclobber, so a second writer fails instead of
#            overwriting, as `lane=<lane> pid=<pid> since=<time> version=<version>`, reads it back after two seconds
#            and refuses unless the line is its own. `annotate` adds ` run=<id> branch=<branch>` to the holder's line
#            once the run exists. `release` renames the lock to theyard-gate.released-<unix time> beside it: its
#            holder (GATE_LANE) always may, a peer only when may_release says yes. `show` prints the line and that
#            verdict. `may_release` judges a line: yes only when its run concluded failure or cancelled, or concluded
#            success and origin/main's changelog top line is the lock's version. A red gate's release frees its
#            number: nothing waits on its branch.
# Does not:  Delete a lock, a branch or a run, push, or fill a version (scripts/fill_version.sh does, after the claim).
#            Let a peer release a green run that has not promoted: that one is its holder's or Steve's. Read a token:
#            gh signs in as whoever runs it.
# Used by:   Whoever ships, around scripts/fill_version.sh and the push (CLAUDE.md, docs/ci-cd/CICD.md).
#
# The lock is GATE_LOCK (the share's theyard-gate.lock by default) and the local flock GATE_FLOCK
# (~/.cache/theyard-gate.flock): every lane on one machine claims through the same local file, so the share's slow
# view of a fresh write never lets two claims through between a read and its read-back.
set -uo pipefail
here=$(cd "$(dirname "$0")" && pwd)
# shellcheck source=scripts/versions.sh
. "$here/versions.sh"
lock=$(gate_lock_path)
local_lock=${GATE_FLOCK:-$HOME/.cache/theyard-gate.flock}
repo=${GITHUB_REPOSITORY:-SteveStout/TheYard}

refuse() {
  echo "gate lock REFUSED: $*"
  exit 1
}

# #region the-local-flock
# Held on descriptor 9 until this process ends: one claim, annotation or release at a time on this machine.
hold_local() {
  mkdir -p "$(dirname "$local_lock")" || refuse "the local lock's folder $(dirname "$local_lock") could not be made"
  exec 9> "$local_lock" || refuse "the local lock $local_lock could not be opened"
  flock -w "${GATE_FLOCK_WAIT:-60}" 9 || refuse "another claim on this machine held $local_lock past its wait; try again"
}
# #endregion the-local-flock

# #region the-release-rule
# origin/main's changelog top line, read from the checkout this runs in.
main_top() {
  git fetch -q origin +refs/heads/main:refs/remotes/origin/main 2> /dev/null || return 1
  git show origin/main:docs/CHANGELOG.md 2> /dev/null | versions | sed -n 1p
}

# Whether a peer may release the lock this line describes: prints yes or no with the reason, and never fails.
may_release() {
  local line=$1 run branch sha version state status conclusion main
  run=$(lock_field "$line" run)
  branch=$(lock_field "$line" branch)
  sha=$(lock_field "$line" sha)
  version=$(lock_field "$line" version)
  if [ -z "$run" ]; then
    # The run id exists only after the push; until the line carries it, the run is found by its commit.
    if [ -z "$sha" ] && [ -n "$branch" ]; then
      sha=$(git ls-remote origin "refs/heads/${branch#refs/heads/}" 2> /dev/null | cut -f1)
    fi
    if [ -z "$sha" ]; then
      echo "no: the line names no run, and no branch or commit to find one by, so nothing says its gate has ended"
      return 0
    fi
    run=$(gh run list -R "$repo" --workflow gate.yml --commit "$sha" --limit 1 --json databaseId --jq '.[0].databaseId // empty' 2> /dev/null)
    if [ -z "$run" ]; then
      echo "no: no gate run is listed for ${sha:0:8} yet"
      return 0
    fi
  fi
  if ! state=$(gh run view "$run" -R "$repo" --json status,conclusion --jq '.status + " " + .conclusion' 2> /dev/null); then
    echo "no: gate run $run could not be read"
    return 0
  fi
  read -r status conclusion <<< "$state"
  if [ "$status" != completed ]; then
    echo "no: gate run $run is ${status:-in an unknown state}, not ended"
    return 0
  fi
  case "$conclusion" in
    failure | cancelled) echo "yes: gate run $run concluded $conclusion, so ${version:-its number} is free again" ;;
    success)
      main=$(main_top)
      if [ -n "$version" ] && [ "$main" = "$version" ]; then
        echo "yes: gate run $run concluded success and main is on $version"
      else
        echo "no: gate run $run is green and main is on ${main:-a version that could not be read}, not ${version:-the version the lock names}: a green run not yet promoted is its holder's or Steve's to release, never a peer's"
      fi
      ;;
    *) echo "no: gate run $run concluded ${conclusion:-nothing}, which is neither red nor promoted" ;;
  esac
}
# #endregion the-release-rule

# #region the-claim
claim() {
  local lane=$1 version=$2 line held
  [[ "$lane" =~ ^[A-Za-z0-9_-]+$ ]] || refuse "'$lane' is not a lane name: letters, digits, hyphens and underscores"
  [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || refuse "'$version' is not a version of four numbers"
  hold_local
  [ -d "$(dirname "$lock")" ] || refuse "the lock's folder $(dirname "$lock") cannot be reached"
  if held=$(head -n 1 "$lock" 2> /dev/null); then
    refuse "the gate lock is held: $held. bash scripts/gate_lock.sh show says whether a peer may release it"
  fi
  line="lane=$lane pid=${GATE_PID:-$PPID} since=$(date --iso-8601=seconds) version=$version"
  (
    set -o noclobber
    printf '%s\n' "$line" > "$lock"
  ) 2> /dev/null || refuse "another lane created the lock first: $(head -n 1 "$lock" 2> /dev/null)"
  sleep "${GATE_READ_BACK_SECONDS:-2}"
  held=$(head -n 1 "$lock" 2> /dev/null) || refuse "the lock could not be read back"
  [ "$held" = "$line" ] || refuse "the lock reads back as another lane's line: $held. It is not yours: fill and push nothing"
  echo "claimed: $line"
}
# #endregion the-claim

# #region the-annotation
annotate() {
  local run=$1 branch=$2 line holder named tail lane base
  [[ "$run" =~ ^[0-9]+$ ]] || refuse "'$run' is not a run id"
  branch=${branch#refs/heads/}
  branch="gate/${branch#gate/}"
  hold_local
  line=$(head -n 1 "$lock" 2> /dev/null) || refuse "no gate lock is held, so there is no line to annotate"
  holder=$(lock_field "$line" lane)
  named=$(lock_field "$line" version)
  [ "$(branch_version "$branch")" = "$named" ] || refuse "the branch names $(branch_version "$branch") and the lock names $named"
  # The lane a branch names sits between the version and the commit: gate/<version>-<lane>-<sha8>.
  tail=${branch#gate/}
  lane=${GATE_LANE:-}
  if [ -z "$lane" ] && [[ "$tail" == *-*-* ]]; then
    lane=${tail#*-}
    lane=${lane%-*}
  fi
  [ -n "$lane" ] || refuse "the branch names no lane, so set GATE_LANE to yours"
  [ "$holder" = "$lane" ] || refuse "the lock is held by $holder, not $lane: annotate only your own line"
  base=$(tr ' ' '\n' <<< "$line" | grep -vE '^(run|branch)=' | paste -sd ' ')
  printf '%s run=%s branch=%s\n' "$base" "$run" "$branch" > "$lock" || refuse "the lock could not be written"
  echo "annotated: $(head -n 1 "$lock")"
}
# #endregion the-annotation

# #region the-release
release() {
  local line holder verdict to
  hold_local
  if ! line=$(head -n 1 "$lock" 2> /dev/null); then
    echo "no gate lock is held: nothing to release"
    return 0
  fi
  holder=$(lock_field "$line" lane)
  if [ -z "${GATE_LANE:-}" ] || [ "$holder" != "$GATE_LANE" ]; then
    verdict=$(may_release "$line")
    case "$verdict" in
      yes*) echo "a peer releases ${holder:-the holder}'s lock: $verdict" ;;
      *) refuse "the lock is ${holder:-another lane}'s and a peer may not release it ($verdict); its holder or Steve releases it" ;;
    esac
  fi
  to="${lock%.lock}.released-$(date +%s)"
  mv "$lock" "$to" || refuse "the lock could not be renamed to $to"
  echo "released: $line, now $(basename "$to")"
}
# #endregion the-release

case "${1:-}" in
  claim) claim "${2:?usage: gate_lock.sh claim <lane> <version>}" "${3:?usage: gate_lock.sh claim <lane> <version>}" ;;
  annotate) annotate "${2:?usage: gate_lock.sh annotate <run-id> <branch>}" "${3:?usage: gate_lock.sh annotate <run-id> <branch>}" ;;
  release) release ;;
  show)
    if line=$(head -n 1 "$lock" 2> /dev/null); then
      echo "held: $line"
      echo "a peer may release it: $(may_release "$line")"
    else
      echo "free: no gate lock at $lock"
    fi
    ;;
  may_release | may-release) may_release "${2:?usage: gate_lock.sh may_release <lock-line>}" ;;
  *)
    echo "usage: gate_lock.sh claim <lane> <version> | annotate <run-id> <branch> | release | show | may_release <lock-line>" >&2
    exit 2
    ;;
esac
