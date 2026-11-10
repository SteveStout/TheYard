"""coderabbit_review.py <branch> <sha> <ceiling-seconds> <out.json> | fold <row.json> <log.json>. Waits for CodeRabbit's review of the gate's pull
request and writes one row of the findings log (ADR-129). Finds the open pull request from <branch>, then waits for the
newest CodeRabbit check run on <sha> to complete, a skipped one counted as not yet, until <ceiling-seconds> have passed.
Then reads CodeRabbit's reviews and inline comments on the request and counts the findings by severity. Every gh answer
is read into a value, and an error or an empty answer is another wait, never a completed review. Writes <out.json>
whatever happened, with "reviewed" saying which: full, summary only, rate limited, completed with its findings not
read, or why it was not reviewed; `missing <version> <sha> <out.json>` writes the row for a read that wrote none. At Level 0
no finding holds a ship, so this always exits 0; the gate's coderabbit job runs it and promote.sh folds the row into
data/coderabbit-findings.json with `fold`, which replaces a row of the same version rather than adding a second."""
import json, os, re, subprocess, sys, time, datetime

BOT = "coderabbitai[bot]"
SEVERITIES = ["critical", "major", "minor", "trivial", "nitpick"]
REPO = os.environ.get("GITHUB_REPOSITORY", "SteveStout/TheYard")


def gh(path):
    """One read of the GitHub API, all pages; None on any error so the caller waits rather than concludes."""
    try:
        out = subprocess.run(["gh", "api", "--paginate", "--slurp", path], capture_output=True, text=True, timeout=60)
    except (subprocess.TimeoutExpired, OSError):
        return None
    if out.returncode != 0 or not out.stdout.strip():
        return None
    try:
        pages = json.loads(out.stdout)
    except ValueError:
        return None
    if all(isinstance(p, list) for p in pages):
        return [item for p in pages for item in p]
    return pages[0] if len(pages) == 1 else pages


def severity(body):
    """The severity CodeRabbit wrote in an inline comment's opening lines, or nitpick when it names none."""
    head = body[:400].lower()
    for name in ["critical", "major", "minor", "trivial"]:
        if name in head:
            return name
    return "nitpick"


def count(reviews, comments):
    """Findings by severity: each inline comment once, plus the nitpicks a review body folds into its own count."""
    found = {name: 0 for name in SEVERITIES}
    for c in comments:
        found[severity(c.get("body") or "")] += 1
    actionable = 0
    for r in reviews:
        body = r.get("body") or ""
        m = re.search(r"Actionable comments posted:\s*(\d+)", body)
        if m:
            actionable += int(m.group(1))
        n = re.search(r"Nitpick comments \((\d+)\)", body)
        if n:
            found["nitpick"] += int(n.group(1))
    return found, actionable


def level():
    """The ladder's current level, read from the record that names it; None when the record cannot be read."""
    folder = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "decisions")
    try:
        for name in os.listdir(folder):
            if name.startswith("ADR-129-"):
                with open(os.path.join(folder, name), encoding="utf-8") as f:
                    m = re.search(r"^Current level: (\d+)", f.read(), re.M)
                    return int(m.group(1)) if m else None
    except OSError:
        return None
    return None


def empty_row(version, sha, reviewed):
    """A row with nothing read yet, so every ship has one whatever stopped the read."""
    return {
        "version": version,
        "pr": None,
        "sha": sha[:8],
        "level": level(),
        "at": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "waited_seconds": None,
        "review_seconds": None,
        "conclusion": None,
        "reviewed": reviewed,
        "actionable": 0,
        "findings": {name: 0 for name in SEVERITIES},
    }


def fold(row_path, log_path):
    """One ship's row into the findings log, newest last; a re-run of a version replaces its row."""
    with open(row_path, encoding="utf-8") as f:
        row = json.load(f)
    log = {"about": "One row per ship: CodeRabbit's review of the gate's pull request, the evidence for every rung of the ladder (ADR-129).", "rows": []}
    if os.path.exists(log_path):
        with open(log_path, encoding="utf-8") as f:
            log = json.load(f)
    log["rows"] = [r for r in log["rows"] if r.get("version") != row.get("version")] + [row]
    with open(log_path, "w", encoding="utf-8") as f:
        json.dump(log, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"findings log: {row.get('version')} {row.get('reviewed')}, {len(log['rows'])} rows")


def main():
    if sys.argv[1] == "fold":
        fold(sys.argv[2], sys.argv[3])
        return
    if sys.argv[1] == "missing":
        with open(sys.argv[4], "w", encoding="utf-8") as f:
            json.dump(empty_row(sys.argv[2], sys.argv[3], "not reviewed, the read wrote no row"), f, indent=2)
            f.write("\n")
        return
    branch, sha, ceiling, out = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]
    start = time.monotonic()
    pr, run = None, None
    while time.monotonic() - start < ceiling:
        if pr is None:
            owner = REPO.split("/")[0]
            pulls = gh(f"repos/{REPO}/pulls?state=open&head={owner}:{branch}")
            if pulls:
                pr = pulls[0]["number"]
        if pr is not None:
            answer = gh(f"repos/{REPO}/commits/{sha}/check-runs?check_name=CodeRabbit")
            runs = [r for r in (answer or {}).get("check_runs", []) if (r.get("app") or {}).get("slug") == "coderabbitai"]
            if runs:
                newest = max(runs, key=lambda r: r.get("started_at") or "")
                words = ((newest.get("output") or {}).get("title") or "") + " " + ((newest.get("output") or {}).get("summary") or "")
                if newest.get("status") == "completed" and "skipped" not in words.lower():
                    run = newest
                    break
        time.sleep(20)
    row = empty_row(branch.removeprefix("gate/"), sha, None)
    row["pr"] = pr
    row["waited_seconds"] = int(time.monotonic() - start)
    if pr is None:
        row["reviewed"] = "not reviewed, no pull request"
    elif run is None:
        row["reviewed"] = "not reviewed, no completed review inside the ceiling"
    else:
        row["conclusion"] = run.get("conclusion")
        fmt = "%Y-%m-%dT%H:%M:%SZ"
        if run.get("started_at") and run.get("completed_at"):
            began = datetime.datetime.strptime(run["started_at"], fmt)
            ended = datetime.datetime.strptime(run["completed_at"], fmt)
            row["review_seconds"] = int((ended - began).total_seconds())
        words = (((run.get("output") or {}).get("title") or "") + " " + ((run.get("output") or {}).get("summary") or "")).lower()
        read_reviews = gh(f"repos/{REPO}/pulls/{pr}/reviews")
        read_comments = gh(f"repos/{REPO}/pulls/{pr}/comments")
        reviews = [r for r in (read_reviews or []) if (r.get("user") or {}).get("login") == BOT]
        comments = [c for c in (read_comments or []) if (c.get("user") or {}).get("login") == BOT]
        row["findings"], row["actionable"] = count(reviews, comments)
        if read_reviews is None or read_comments is None:
            row["reviewed"] = "completed, findings not read"
        elif "rate limit" in words:
            row["reviewed"] = "rate limited"
        elif reviews or comments or "review completed" in words:
            row["reviewed"] = "full"
        else:
            row["reviewed"] = "summary only"
    with open(out, "w", encoding="utf-8") as f:
        json.dump(row, f, indent=2)
        f.write("\n")
    print(json.dumps(row))
    if row["reviewed"].startswith("not reviewed") or row["reviewed"].endswith("not read"):
        print(f"::notice::CodeRabbit: {row['reviewed']}; Level 0 ships anyway (ADR-129)")


if __name__ == "__main__":
    main()
