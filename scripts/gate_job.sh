#!/usr/bin/env bash
# gate_job.sh <raw folder> <key> [<key> ...]
#
# Does:      One check job's own verdict, the last step of each check job in the gate (ADR-098, the addendum of
#            1.0.5.4): every key named has a result in the job's key=value result files, and it is 0, or skipped
#            where the two cuts skipped it. A key with no result is red, so a job that died before it wrote one is
#            red by its own name. Exits non-zero on any red, so the run's email names the job and a re-run of the
#            failed jobs runs this job again.
# Does not:  Decide the run: the gate job folds every job's reports and its verdict is the one promote reads. Read
#            the code's own numbers, which are measurements and never red.
# Used by:   .github/workflows/gate.yml, the dotnet, cosmos, node and browser jobs.
set -uo pipefail
raw=$1
shift
red=""
for key in "$@"; do
  value=$({ cat "$raw"/dotnet.result "$raw"/node.result "$raw"/rest.result "$raw"/build.result 2>/dev/null || true; } | grep -E "^$key=" | head -n 1 | cut -d= -f2)
  case "$value" in
    0 | skipped) echo "$key=$value" ;;
    "") red="$red $key" && echo "$key: no result" ;;
    *) red="$red $key" && echo "$key=$value" ;;
  esac
done
if [ -n "$red" ]; then
  echo "::error::red in this job:$red"
  exit 1
fi
echo "this job: nothing red"
