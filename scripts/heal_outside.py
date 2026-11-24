#!/usr/bin/env python3
"""heal_outside.py [--branch <name>] [--base <ref>] [--head <commit>] [--changes <file>]

Does:      The gate's half of the outside self-heal list (the year-end plan's 7b; selfheal1120 version 5): on a heal
           branch, ready/heal-* (a heal lane's GATE_LANE starts heal-), every changed path is matched against
           data/heal-outside.json, where * crosses folders, and a match, or any deleted file, is red. Any other branch
           is not a heal and passes. The changes are `git diff --name-status <merge-base> <head>`, or a file in that
           shape (--changes, for its tests). --head is the heal's own commit, HEAD unless named: the gate names the
           ready commit the cop published, before its fill, so the changelog, counts and ratchet the cop writes for
           every version are never read as the heal's. Exits 1 on red and names each path with its rule.
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


def changes(base, head, path):
    if path:
        text = Path(path).read_text(encoding="utf-8")
    else:
        start = subprocess.run(["git", "merge-base", base, head], capture_output=True, text=True, check=True, cwd=ROOT).stdout.strip()
        text = subprocess.run(["git", "diff", "--name-status", start, head], capture_output=True, text=True, check=True, cwd=ROOT).stdout
    for line in text.splitlines():
        cells = line.split("\t")
        if len(cells) >= 2:
            yield cells[0][:1], cells[-1], cells[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--branch", default=os.environ.get("GITHUB_REF_NAME", ""))
    parser.add_argument("--base", default="origin/main")
    parser.add_argument("--head", default="HEAD")
    parser.add_argument("--changes")
    args = parser.parse_args()
    lane = os.environ.get("GATE_LANE", "")
    if not is_heal(args.branch, lane):
        print(f"heal-outside: not a heal branch ({args.branch or lane or 'unnamed'}), nothing held")
        return 0
    rules = json.loads((ROOT / "data" / "heal-outside.json").read_text(encoding="utf-8"))["paths"]
    red = []
    try:
        changed = list(changes(args.base, args.head, args.changes))
    except subprocess.CalledProcessError as error:
        print(f"::error::heal-outside: the changes from {args.base} to {args.head} could not be read: {error.stderr.strip()}")
        return 1
    for status, path, source in changed:
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
