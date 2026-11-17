#!/usr/bin/env bash
# emdash_scan.sh
#
# Does:      The house rule on em dashes, over every text file of the checkout: lists each file that holds one and
#            exits 1 when there is any. One command with two callers, the gate and preflight's catch list, so the
#            check a lane runs before its claim can never drift from the one that turns the gate red.
# Does not:  Change a file, or look inside node_modules, build output or .git.
# Used by:   scripts/catch_list.sh (preflight), and the gate's em dash step.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
hits=$(grep -rlP '\x{2014}' --include='*.md' --include='*.cs' --include='*.ts' --include='*.tsx' --include='*.css' --include='*.yml' --include='*.yaml' --include='*.json' --include='*.sql' --include='*.sqlproj' --include='*.csproj' --include='*.slnx' --include='*.mjs' --include='*.svg' --exclude-dir=node_modules --exclude-dir=bin --exclude-dir=obj --exclude-dir=.git --exclude-dir=dist --exclude-dir=dist-render --exclude-dir=playwright-report --exclude-dir=test-results . || true)
if [ -n "$hits" ]; then
  echo "em dashes in $(wc -l <<< "$hits") file(s):"
  sed 's/^/  /' <<< "$hits"
  exit 1
fi
echo "em dashes: none"
