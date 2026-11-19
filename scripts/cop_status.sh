#!/usr/bin/env bash
# cop_status.sh <sha> <success|pending|failure> <words>
#
# Does:      Posts what became of a ready branch as a commit status named "cop" on the commit its lane pushed (ADR-137,
#            One traffic cop for main): the words cut to the 140 characters GitHub keeps, and the run's own page as the
#            link, so a lane that reads "skipped" or "red gate" opens the run that said it.
# Does not:  Decide the words (gate.yml's cop and said jobs do), retry, or read, print or pass a token value: gh signs
#            in with the run's own token, which needs statuses: write.
# Used by:   .github/workflows/gate.yml, the cop job (a skip) and the said job (landed, a hand promote owed, red).
set -euo pipefail
sha=${1:?usage: cop_status.sh <sha> <state> <words>}
state=${2:?usage: cop_status.sh <sha> <state> <words>}
words=${3:?usage: cop_status.sh <sha> <state> <words>}
repo=${GITHUB_REPOSITORY:-SteveStout/TheYard}
link="${GITHUB_SERVER_URL:-https://github.com}/$repo/actions/runs/${GITHUB_RUN_ID:-}"
gh api -X POST "repos/$repo/statuses/$sha" -f state="$state" -f context=cop -f description="${words:0:140}" -f target_url="$link" > /dev/null
echo "cop status on ${sha:0:8}: $state, ${words:0:140}"
