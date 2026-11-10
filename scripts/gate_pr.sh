#!/usr/bin/env bash
# gate_pr.sh open <version> | close <version> <main-sha>
#
# Does:      The gate's pull request, so CodeRabbit reviews every ship (ADR-129). `open` runs on the machine that
#            ships, right after the push of gate/<version>: it opens a pull request from that branch into main
#            titled with the commit's subject, or reuses the open one, and asks CodeRabbit for a review with a
#            comment, which a repository under ten stars needs. `close` runs once main holds the version: it
#            closes the pull request with a comment naming main's commit, because promote.sh amends the gate's
#            commit before it fast-forwards main and GitHub therefore never marks the request merged.
# Does not:  Merge anything, push anything or wait for the review: the gate's coderabbit job waits, and main only
#            ever moves by promote.sh's fast-forward. Read, print or pass a token value: gh signs in as whoever
#            runs it (the owner on the shipping machine, the run's own token in the gate).
# Used by:   Whoever ships, after `git push origin HEAD:gate/<version>` (docs/ci-cd/CICD.md); gate.yml's
#            after-roll job and a person promoting by hand, for `close`.
set -euo pipefail
repo=${GITHUB_REPOSITORY:-SteveStout/TheYard}
action=${1:-}
version=${2:-}
[ -n "$version" ] || {
  echo "usage: gate_pr.sh open <version> | close <version> <main-sha>" >&2
  exit 2
}
branch="gate/$version"
open_pr() { gh pr list -R "$repo" --head "$branch" --state open --json number --jq '.[0].number // empty'; }

case "$action" in
  open)
    subject=$(gh api "repos/$repo/commits/$branch" --jq '.commit.message | split("\n")[0]') || subject=""
    [ -n "$subject" ] || {
      echo "gate_pr: $branch could not be read on $repo (gh's error above); if it was not pushed, push it first" >&2
      exit 1
    }
    case "$subject" in
      "TheYard $version: "*) title=$subject ;;
      *) title="TheYard $version: $subject" ;;
    esac
    pr=$(open_pr)
    if [ -z "$pr" ]; then
      gh pr create -R "$repo" --base main --head "$branch" --title "$title" \
        --body "The gate's pull request for $version. CodeRabbit reviews it at the level .coderabbit.yaml names (ADR-129); the gate promotes on its own run and this request is closed with main's commit, never merged here." > /dev/null
      pr=$(open_pr)
    fi
    [ -n "$pr" ] || {
      echo "gate_pr: no open pull request for $branch after create" >&2
      exit 1
    }
    gh pr comment "$pr" -R "$repo" --body "@coderabbitai review" > /dev/null
    echo "pull request $pr for $branch, review asked"
    ;;
  close)
    sha=${3:-}
    [ -n "$sha" ] || {
      echo "usage: gate_pr.sh close <version> <main-sha>" >&2
      exit 2
    }
    pr=$(open_pr)
    if [ -z "$pr" ]; then
      echo "no open pull request for $branch"
      exit 0
    fi
    gh pr close "$pr" -R "$repo" --comment "Promoted: main is ${sha:0:8}, TheYard $version. The gate amends its commit with the run's results before it fast-forwards main, so this request is closed with that commit rather than merged."
    echo "pull request $pr closed on ${sha:0:8}"
    ;;
  *)
    echo "usage: gate_pr.sh open <version> | close <version> <main-sha>" >&2
    exit 2
    ;;
esac
