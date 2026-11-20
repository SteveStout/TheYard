#!/usr/bin/env python3
"""heal_outside.py [--branch <name>] [--base <ref>] [--changes <file>]

Does:      The gate's half of the outside self-heal list (the year-end plan's 7b; selfheal1120 version 5): on a heal
           branch, ready/heal-* (a heal lane's GATE_LANE starts heal-), every changed path is matched against
           data/heal-outside.json, where * crosses folders, and a match, or any deleted file, is red. Any other branch
           is not a heal and passes. The changes are `git diff --name-status <merge-base> HEAD`, or a file in that
           shape (--changes, for its tests). Exits 1 on red and names each path with its rule.
Does not:  Judge the change itself, or stop a planning session's commit from changing the list: only a heal is held.
Used by:   scripts/catch_list.sh (preflight) and .github/workflows/gate.yml, the node job; GateWorkflowTests.
"""
import argparse
import fnmatch
import json
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def is_heal(branch, lane):
    return branch.startswith("ready/heal-") or branch.startswith("heal-") or lane.startswith("heal-")


def changes(base, path):
    if path:
        text = Path(path).read_text(encoding="utf-8")
    else:
        start = subprocess.run(["git", "merge-base", base, "HEAD"], capture_output=True, text=True, check=True, cwd=ROOT).stdout.strip()
        text = subprocess.run(["git", "diff", "--name-status", start, "HEAD"], capture_output=True, text=True, check=True, cwd=ROOT).stdout
    for line in text.splitlines():
        cells = line.split("\t")
        if len(cells) >= 2:
            yield cells[0][:1], cells[-1], cells[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--branch", default=os.environ.get("GITHUB_REF_NAME", ""))
    parser.add_argument("--base", default="origin/main")
    parser.add_argument("--changes")
    args = parser.parse_args()
    lane = os.environ.get("GATE_LANE", "")
    if not is_heal(args.branch, lane):
        print(f"heal-outside: not a heal branch ({args.branch or lane or 'unnamed'}), nothing held")
        return 0
    rules = json.loads((ROOT / "data" / "heal-outside.json").read_text(encoding="utf-8"))["paths"]
    red = []
    for status, path, source in changes(args.base, args.changes):
        if status == "D":
            red.append(f"{path}: deleted (f: a heal moves and never deletes)")
            continue
        for candidate in {path, source}:
            hit = next((rule for rule in rules if fnmatch.fnmatchcase(candidate, rule)), None)
            if hit:
                red.append(f"{candidate}: on the outside list ({hit})")
    for line in red:
        print(f"::error::heal-outside: {line}")
    print(f"heal-outside: {len(red)} paths outside self-heal" if red else "heal-outside: a heal branch, no path on the outside list")
    return 1 if red else 0


if __name__ == "__main__":
    sys.exit(main())
