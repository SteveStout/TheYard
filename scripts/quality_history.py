"""quality_history.py rebuild | append <version> | coverage. The quality history (ADR-130, Quality you can watch):
data/quality-history.json, one row per shipped version from the first ratchet commit (1.0.4.115) on, plus the two
rows the pre-scan measured before quality.py existed. Every row is measured by the scanner this checkout carries,
.github/quality.py, so the whole history is read by one instrument; each row also keeps the limits the ratchet file
held at that version, which is what the gate enforced then. `rebuild` measures every version commit on main from a
clean export of its tree; `append` measures this checkout as <version> and replaces a row of the same version, which
promote.sh runs on every ship; `coverage` fills a row's coverage from the coverage notice CI on main wrote for that
version's commit, where CI has run. Needs git, and gh for coverage; a read that fails leaves the field null and
never stops a ship."""
import datetime, json, os, re, subprocess, sys, tempfile

ROOT = subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True, check=True).stdout.strip()
LOG = os.path.join(ROOT, "data", "quality-history.json")
SCANNER = os.path.join(ROOT, ".github", "quality.py")
REPO = os.environ.get("GITHUB_REPOSITORY", "SteveStout/TheYard")
FIRST = "1.0.4.115"
ABOUT = ("One row per shipped version: the fourteen numbers of the quality ratchet, measured by this checkout's "
         ".github/quality.py, the limits the ratchet file held at that version, the thin spots, and line and branch "
         "coverage from CI on main. Rows marked pre-scan were measured before the scanner existed (ADR-130).")

# #region pre-scan
# The two readings from before the scanner existed, read from the pre-scan of 29 October (its 10/15 and 10/22
# columns). They name nine of the fourteen numbers; the rest were not counted then and stay null.
PRE_SCAN = [
    {"version": None, "date": "2026-10-15", "source": "pre-scan", "numbers": {
        "undocumented_declarations": 239, "wide_service_classes": 0, "async_without_cancellation": 50,
        "clock_outside_composition": 46, "history_or_name_comments": 2, "files_within_10_of_cap": 3,
        "behavior_types_never_named_in_a_test": 6, "test_to_prod_ratio_higher_is_better": 0.83,
        "dotnet_tests_higher_is_better": 928}},
    {"version": None, "date": "2026-10-22", "source": "pre-scan", "numbers": {
        "undocumented_declarations": 348, "wide_service_classes": 2, "async_without_cancellation": 104,
        "clock_outside_composition": 53, "history_or_name_comments": 7, "files_within_10_of_cap": 6,
        "behavior_types_never_named_in_a_test": 7, "test_to_prod_ratio_higher_is_better": 0.78,
        "dotnet_tests_higher_is_better": 1514}},
]
# #endregion pre-scan


def git(*args):
    return subprocess.run(["git", "-C", ROOT, *args], capture_output=True, text=True, check=True).stdout


def measure(tree):
    """The scanner's report over one tree: the ratchet's numbers and the three thin spots the page names."""
    out = subprocess.run([sys.executable, SCANNER, tree], capture_output=True, text=True, check=True).stdout
    report = json.loads(out)
    data = report.get("xml_docs", {}).get("TheYard.Data", {})
    headers = report.get("react_headers", {})
    return report["ratchet"], {
        "react_files": headers.get("files"),
        "react_files_without_header": headers.get("missing"),
        "data_xml_docs_pct": data.get("pct"),
    }


def limits_at(ref):
    try:
        return json.loads(git("show", f"{ref}:.github/quality-ratchet.json"))
    except (subprocess.CalledProcessError, ValueError):
        return None


def versions_on_main():
    """Every version commit on main from the first ratchet commit on, oldest first: (version, sha, date)."""
    found, started = [], False
    for line in git("log", "--reverse", "--first-parent", "--format=%H %cI %s", "origin/main").splitlines():
        sha, when, subject = line.split(" ", 2)
        m = re.match(r"TheYard (\d+\.\d+\.\d+\.\d+): ", subject)
        if not m:
            continue
        started = started or m.group(1) == FIRST
        if started:
            found.append((m.group(1), sha, when))
    return found


def coverage_of(sha):
    """Line and branch coverage from the notice CI on main wrote for <sha>, or None when there is none to read."""
    try:
        runs = json.loads(subprocess.run(["gh", "api", f"repos/{REPO}/actions/workflows/ci.yml/runs?head_sha={sha}&per_page=20"],
                                         capture_output=True, text=True, check=True, timeout=60).stdout)
        for run in runs.get("workflow_runs", []):
            if run.get("head_branch") != "main" or run.get("conclusion") != "success":
                continue
            jobs = json.loads(subprocess.run(["gh", "api", f"repos/{REPO}/actions/runs/{run['id']}/jobs"],
                                             capture_output=True, text=True, check=True, timeout=60).stdout)
            for job in jobs.get("jobs", []):
                if job.get("name") != "api":
                    continue
                notes = json.loads(subprocess.run(["gh", "api", f"repos/{REPO}/check-runs/{job['id']}/annotations"],
                                                  capture_output=True, text=True, check=True, timeout=60).stdout)
                for note in notes:
                    m = re.search(r"coverage ([\d.]+)% of lines, ([\d.]+)% of branches", note.get("message") or "")
                    if m:
                        return {"lines": float(m.group(1)), "branches": float(m.group(2)), "ci_run": run["id"]}
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired, OSError, ValueError):
        return None
    return None


def load():
    if os.path.exists(LOG):
        with open(LOG, encoding="utf-8") as f:
            return json.load(f)
    return {"about": ABOUT, "first_ratchet_version": FIRST, "rows": list(PRE_SCAN)}


def save(log):
    log["about"] = ABOUT
    with open(LOG, "w", encoding="utf-8") as f:
        json.dump(log, f, indent=1, ensure_ascii=False)
        f.write("\n")


def row(version, sha, when, numbers, thin, limits):
    return {"version": version, "date": when[:10], "commit": sha[:8], "source": "quality.py",
            "numbers": numbers, "limits": limits, "thin": thin, "coverage": None}


def rebuild():
    log = {"about": ABOUT, "first_ratchet_version": FIRST, "rows": list(PRE_SCAN)}
    for version, sha, when in versions_on_main():
        with tempfile.TemporaryDirectory() as tree:
            archive = subprocess.run(["git", "-C", ROOT, "archive", sha], capture_output=True, check=True).stdout
            subprocess.run(["tar", "-x", "-C", tree], input=archive, check=True)
            numbers, thin = measure(tree)
        entry = row(version, sha, when, numbers, thin, limits_at(sha))
        entry["coverage"] = coverage_of(sha)
        log["rows"].append(entry)
        print(f"{version} {sha[:8]} {numbers['undocumented_declarations']} {entry['coverage']}", flush=True)
    save(log)


def append(version):
    log = load()
    numbers, thin = measure(ROOT)
    now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    entry = row(version, git("rev-parse", "HEAD").strip(), now, numbers, thin, limits_at("HEAD"))
    log["rows"] = [r for r in log["rows"] if r.get("version") != version] + [entry]
    fill_coverage(log)
    save(log)
    print(f"quality history: {version} measured, {len(log['rows'])} rows")


def fill_coverage(log):
    """Coverage for each measured row that has none yet, from CI on main for that version's commit on main."""
    subjects = {}
    try:
        for line in git("log", "--first-parent", "--format=%H %s", "origin/main", "-n", "400").splitlines():
            sha, subject = line.split(" ", 1)
            m = re.match(r"TheYard (\d+\.\d+\.\d+\.\d+): ", subject)
            if m:
                subjects.setdefault(m.group(1), sha)
    except subprocess.CalledProcessError:
        return
    for entry in log["rows"]:
        if entry.get("source") == "quality.py" and entry.get("coverage") is None and entry.get("version") in subjects:
            entry["coverage"] = coverage_of(subjects[entry["version"]])


if __name__ == "__main__":
    action = sys.argv[1] if len(sys.argv) > 1 else ""
    if action == "rebuild":
        rebuild()
    elif action == "append" and len(sys.argv) > 2:
        append(sys.argv[2])
    elif action == "coverage":
        log = load()
        fill_coverage(log)
        save(log)
    else:
        sys.exit(__doc__)
