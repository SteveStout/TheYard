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

import os
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


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
