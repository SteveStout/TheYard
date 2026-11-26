#!/usr/bin/env bash
# dispatch.sh <workflow> [gh workflow run arguments...]
#
# Does:      Starts a workflow with gh workflow run, and starts it again when GitHub answers 5xx or the call never
#            reached it: a pause that grows by DISPATCH_PAUSE seconds each time (30 by default), DISPATCH_TRIES calls in
#            all (8 by default, about fourteen minutes). One HTTP 504 on the Function's dispatch once failed a whole
#            after-roll, and the hand starts after it met 502 and 500 through twenty minutes of a GitHub outage.
# Does not:  Retry a 4xx (a wrong input or a missing workflow stays wrong), or check whether a call that answered 5xx
#            started a run anyway: every workflow it starts reads its own commit, so a second run of it only repeats.
# Used by:   .github/workflows/after-roll.yml, the edge, the Function and CI on main.
set -uo pipefail
workflow=${1:?usage: dispatch.sh <workflow> [gh workflow run arguments...]}
tries=${DISPATCH_TRIES:-8}
pause=${DISPATCH_PAUSE:-30}
for try in $(seq 1 "$tries"); do
  if answer=$(gh workflow run "$@" 2>&1); then
    echo "$answer"
    echo "$workflow started (call $try of $tries)"
    exit 0
  fi
  echo "$answer"
  if grep -q 'HTTP 4[0-9][0-9]' <<< "$answer"; then
    echo "::error::$workflow refused with a 4xx; starting it again would be refused the same way"
    exit 1
  fi
  [ "$try" -lt "$tries" ] && { echo "$workflow did not start (call $try of $tries); again in $((pause * try)) s"; sleep $((pause * try)); }
done
echo "::error::$workflow did not start in $tries calls"
exit 1
