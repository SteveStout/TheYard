#!/usr/bin/env python3
"""
Read coverlet's Cobertura report, say what it found, and hold it to a floor.

Written as a script rather than an action so a reader can see exactly what the
number means, and so the same command runs locally and on the runner.

It writes three things: a job summary for somebody signed in, GitHub
annotations for a reader who is not, and an exit code for the build. The
annotations matter because a red run whose only public words are "exit code 1"
costs somebody a session to understand, which this repository has paid for twice
(ADR: The exemption that hid a contrast failure, addendum).

Usage: python3 .github/coverage.py <results-dir> <line-floor> <branch-floor>
"""

import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


# The blog's slice is held to a floor of its own, higher than the whole suite's (ADR: Every LinkedIn post is also a
# blog post, the addendum on the read model): every file under a Features/Blog folder in any ring, the blog's model
# and the security headers, counted line by line and branch by branch from the same report. The Data ring's records
# and the row types hold no behaviour, so they are left out. The floor lives in .github/blog-coverage.json, and like
# every floor here it only moves up, in the commit that earned it.
BLOG_FILES = re.compile(r"/Features/Blog/|/BlogModel\.cs$|/SecurityHeaders\.cs$")
BLOG_FLOOR = Path(__file__).with_name("blog-coverage.json")


def blog_slice(root) -> tuple[float, float]:
    # Coverlet writes a line's branches once for each method on it (a lambda is a method of its own) and copies them
    # into the class's own lines, so lines are counted from the class's list and branches from the methods' only.
    seen, lines, hit, branches, taken = set(), 0, 0, 0, 0
    for cls in root.iter("class"):
        name = cls.get("filename", "").replace("\\", "/")
        if "/TheYard.Data/" in name or name.endswith("BlogRows.cs") or not BLOG_FILES.search(name):
            continue
        for line in cls.findall("lines/line"):
            key = (name, line.get("number"))
            if key in seen:
                continue
            seen.add(key)
            lines += 1
            hit += int(line.get("hits", "0")) > 0
        for method in cls.findall("methods/method"):
            for line in method.findall("lines/line"):
                key = (name, method.get("name"), method.get("signature"), line.get("number"))
                counted = re.search(r"\((\d+)/(\d+)\)", line.get("condition-coverage", ""))
                if key in seen or not counted:
                    continue
                seen.add(key)
                taken += int(counted.group(1))
                branches += int(counted.group(2))
    return 100 * hit / max(lines, 1), 100 * taken / max(branches, 1)


def report(results_dir: str, line_floor: float, branch_floor: float) -> int:
    reports = sorted(Path(results_dir).rglob("coverage.cobertura.xml"))
    if not reports:
        print(f"::error::no coverage report under {results_dir}")
        return 1

    # One test project, so one report. Reading the newest rather than merging
    # keeps this honest: if that ever stops being true, the count will look
    # wrong rather than silently average two things.
    root = ET.parse(reports[-1]).getroot()
    lines = float(root.get("line-rate", 0)) * 100
    branches = float(root.get("branch-rate", 0)) * 100

    rows = []
    for package in root.iter("package"):
        rows.append(
            (
                package.get("name", "?"),
                float(package.get("line-rate", 0)) * 100,
                float(package.get("branch-rate", 0)) * 100,
            )
        )
    rows.sort(key=lambda row: row[1])

    summary = [
        "## Coverage",
        "",
        f"**{lines:.1f}%** of lines and **{branches:.1f}%** of branches,"
        f" against floors of {line_floor:.0f}% and {branch_floor:.0f}%.",
        "",
        "| Project | Lines | Branches |",
        "| --- | ---: | ---: |",
    ]
    for name, line_rate, branch_rate in rows:
        summary.append(f"| {name} | {line_rate:.1f}% | {branch_rate:.1f}% |")

    where = os.environ.get("GITHUB_STEP_SUMMARY")
    if where:
        with open(where, "a", encoding="utf-8") as handle:
            handle.write("\n".join(summary) + "\n")
    print("\n".join(summary))

    failed = False
    floor = json.loads(BLOG_FLOOR.read_text(encoding="utf-8"))
    blog_lines, blog_branches = blog_slice(root)
    print(f"blog slice: {blog_lines:.1f}% of lines, {blog_branches:.1f}% of branches (floors {floor['lines']}% and {floor['branches']}%)")
    if blog_lines < floor["lines"] or blog_branches < floor["branches"]:
        print(f"::error::the blog slice's coverage, {blog_lines:.1f}% of lines and {blog_branches:.1f}% of branches, is under its floor in {BLOG_FLOOR.name}")
        failed = True
    if lines < line_floor:
        print(f"::error::line coverage {lines:.1f}% is under the {line_floor:.0f}% floor")
        failed = True
    if branches < branch_floor:
        print(f"::error::branch coverage {branches:.1f}% is under the {branch_floor:.0f}% floor")
        failed = True
    if not failed:
        print(f"::notice::coverage {lines:.1f}% of lines, {branches:.1f}% of branches")
    return 1 if failed else 0


if __name__ == "__main__":
    if len(sys.argv) != 4:
        print(__doc__)
        sys.exit(2)
    sys.exit(report(sys.argv[1], float(sys.argv[2]), float(sys.argv[3])))
