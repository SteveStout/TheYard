#!/usr/bin/env python3
"""gate_fold.py <downloads> <raw> <attempt> <job> [<job> ...]

Does:      Folds the raw reports of the gate's check jobs into the one gate-raw folder the verdict, promote.sh,
           the after-roll and the Admin tab read, in the shape they read before the gate was split (ADR-098, the
           addendum of 1.0.5.4). Each job uploads raw-<job>-<attempt>; for every job named here this takes the
           newest attempt at or below <attempt>, so a job re-run green replaces its red attempt and a job that was
           not re-run keeps the attempt it ran in. A key=value result file written by more than one job is merged
           by key (a red value wins, then a pass, then a skip; a time is the longest), the two browser shards'
           Playwright reports become one, and their text logs are joined. Writes fold=0 to rest.result when every
           job named here left its reports and every job in $GATE_NEEDS (the workflow's needs, as JSON) succeeded,
           and fold=1 when any did not (fold.txt names which), which the verdict reads: half the browser suite must
           never read as the whole of it, and a job that died is red whatever it left behind.
Does not:  Judge a check (the verdict does), run one, or read anything outside <downloads>.
Used by:   .github/workflows/gate.yml, the gate job; GateFoldTests.
"""
import json
import os
import re
import shutil
import sys
from pathlib import Path

ARTIFACT = re.compile(r"^raw-(?P<job>.+)-(?P<attempt>\d+)$")


def newest(downloads, attempt, jobs):
    """The folder each named job's newest attempt at or below this one left, or None where it left none."""
    found = {}
    for folder in sorted(p for p in Path(downloads).iterdir() if p.is_dir()):
        match = ARTIFACT.match(folder.name)
        if not match or int(match["attempt"]) > attempt:
            continue
        job, at = match["job"], int(match["attempt"])
        if job in jobs and (job not in found or at > found[job][0]):
            found[job] = (at, folder)
    return {job: found.get(job, (None, None))[1] for job in jobs}


def read_result(path):
    pairs = []
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        key, sep, value = line.partition("=")
        if sep and key.strip():
            pairs.append((key.strip(), value.strip()))
    return pairs


def merged(values, key):
    """One value from several jobs' answers for the same key."""
    if key.endswith("_s"):
        numbers = [int(v) for v in values if v.lstrip("-").isdigit()]
        return str(max(numbers)) if numbers else values[0]
    red = [v for v in values if v not in ("0", "skipped")]
    if red:
        return red[0]
    return "0" if "0" in values else "skipped"


def fold_results(sources, out):
    keys, values = [], {}
    for path in sources:
        for key, value in read_result(path):
            if key not in values:
                keys.append(key)
                values[key] = []
            values[key].append(value)
    out.write_text("".join(f"{key}={merged(values[key], key)}\n" for key in keys), encoding="utf-8")


def fold_playwright(sources, out):
    """Two shards' JSON reports as one: every file's suites, and the counts added up."""
    reports = [json.loads(p.read_text(encoding="utf-8-sig")) for p in sources]
    whole = dict(reports[0])
    whole["suites"] = [suite for report in reports for suite in report.get("suites", [])]
    whole["errors"] = [error for report in reports for error in report.get("errors", [])]
    stats = {}
    for report in reports:
        for key, value in (report.get("stats") or {}).items():
            if isinstance(value, (int, float)) and not isinstance(value, bool):
                stats[key] = max(stats.get(key, 0), value) if key == "duration" else stats.get(key, 0) + value
            else:
                stats.setdefault(key, value)
    whole["stats"] = stats
    out.write_text(json.dumps(whole), encoding="utf-8")


def main(argv):
    if len(argv) < 5:
        print(__doc__.splitlines()[0], file=sys.stderr)
        return 2
    downloads, raw, attempt, jobs = Path(argv[1]), Path(argv[2]), int(argv[3]), argv[4:]
    raw.mkdir(parents=True, exist_ok=True)
    chosen = newest(downloads, attempt, jobs)
    files = {}
    for job in jobs:
        folder = chosen[job]
        if folder is None:
            continue
        for path in sorted(p for p in folder.rglob("*") if p.is_file()):
            files.setdefault(path.relative_to(folder).as_posix(), []).append(path)
    for name, sources in files.items():
        target = raw / name
        target.parent.mkdir(parents=True, exist_ok=True)
        if name.endswith(".result"):
            fold_results(sources, target)
        elif len(sources) > 1 and name.endswith(".json") and name.startswith("browser-"):
            fold_playwright(sources, target)
        elif len(sources) > 1 and name.endswith(".txt") and name != "touched.txt":
            target.write_text("".join(p.read_text(encoding="utf-8", errors="replace") for p in sources), encoding="utf-8")
        else:
            shutil.copyfile(sources[0], target)
    missing = [job for job in jobs if chosen[job] is None]
    needs = json.loads(os.environ.get("GATE_NEEDS") or "{}")
    failed = sorted(name for name, need in needs.items() if need.get("result") != "success")
    with (raw / "rest.result").open("a", encoding="utf-8") as rest:
        rest.write("fold=1\n" if missing or failed else "fold=0\n")
    lines = [f"{job}: {chosen[job].name if chosen[job] else 'NO REPORTS'}\n" for job in jobs]
    lines += [f"job {name}: {needs[name].get('result')}\n" for name in failed]
    (raw / "fold.txt").write_text("".join(lines), encoding="utf-8")
    print((raw / "fold.txt").read_text(encoding="utf-8"), end="")
    if missing:
        print(f"::error::the gate's fold found no reports from: {' '.join(missing)}")
    if failed:
        print(f"::error::these check jobs did not succeed: {' '.join(failed)}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
