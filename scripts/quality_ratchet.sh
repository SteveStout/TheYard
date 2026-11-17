#!/usr/bin/env bash
# quality_ratchet.sh
#
# Does:      The quality ratchet, the same command CI and the gate run (ADR: The numbers that may only fall): every
#            number .github/quality.py measures on the checkout against .github/quality-ratchet.json, exiting non-zero
#            when one is worse. One command with two callers, the gate and preflight's catch list.
# Does not:  Build anything or change the ratchet's file: a number is tightened only by the fill or by hand.
# Used by:   scripts/catch_list.sh (preflight), and the gate's ratchet step.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
python3 .github/quality.py . --ratchet .github/quality-ratchet.json
