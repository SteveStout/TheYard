"""coderabbit_review.py <branch> <sha> <ceiling-seconds> <out.json> | fold <row.json> <log.json> | count <comments.json>
| missing <version> <sha> <out.json> | recount <version> <branch> <sha> <log.json>. Waits for CodeRabbit's review of the gate's pull request and writes one row of the findings log (ADR-129). Finds the open pull request from <branch>, then waits for the
newest CodeRabbit check run on <sha> to complete, a skipped one counted as not yet, until <ceiling-seconds> have passed.
Then reads CodeRabbit's reviews and inline comments on every request that carried the commit, because a branch pushed again
opens a second request and CodeRabbit does not review a commit twice, and counts the findings by severity on the request
that holds the review. Every gh answer
is read into a value, and an error or an empty answer is another wait, never a completed review. Writes <out.json>
whatever happened, with "reviewed" saying which: full, summary only, rate limited, completed with its findings not
read, or why it was not reviewed; `missing` writes the row for a read that wrote none, and `count` prints the findings a
file of inline comments holds, each by its header label, for one commit when a sha follows the file; `recount` reads a
version's commit again across every request that carried it and rewrites that row's findings. At Level 0 no finding holds a ship, so this always exits 0; the gate's coderabbit job runs it and promote.sh folds the row into
data/coderabbit-findings.json with `fold`, which replaces a row of the same version rather than adding a second."""
import json, os, re, subprocess, sys, time, datetime

BOT = "coderabbitai[bot]"
SEVERITIES = ["critical", "major", "minor", "trivial", "nitpick"]
REPO = os.environ.get("GITHUB_REPOSITORY", "SteveStout/TheYard")
NO_REQUEST_AFTER = 300


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


# The label CodeRabbit writes at the head of a finding: "<picture>...</picture> Minor**" in its current header, or
# "_Minor_" in the older one. Only the label is read, so a finding whose text mentions another severity is not
# counted as that one.
LABEL = re.compile(r"(?:</picture>\s*|_[^_\n]*?)(Critical|Major|Minor|Trivial)(?:\*\*|_)")


def severity(body):
    """The severity in an inline comment's header label, or nitpick when the header carries none. The header is the
    first line that is not blank and not an HTML comment; the remark under it is never read for a label."""
    header = next((line for line in body.splitlines() if line.strip() and not line.strip().startswith("<!--")), "")
    m = LABEL.search(header)
    return m.group(1).lower() if m else "nitpick"


def count(reviews, comments, sha=None):
    """Findings by severity on one commit: each inline comment that opens a thread once (a reply in a thread is not
    a finding), plus the nitpicks a review body folds into its own count. With <sha>, only that commit's review and
    comments count, so a second push to the same pull request does not count the first commit's findings again."""
    found = {name: 0 for name in SEVERITIES}
    for c in comments:
        if c.get("in_reply_to_id") or (sha and c.get("commit_id") not in (None, sha)):
            continue
        found[severity(c.get("body") or "")] += 1
    actionable = 0
    for r in reviews:
        if sha and r.get("commit_id") not in (None, sha):
            continue
        body = r.get("body") or ""
        m = re.search(r"Actionable comments posted:\s*(\d+)", body)
        if m:
            actionable += int(m.group(1))
        n = re.search(r"Nitpick comments \((\d+)\)", body)
        if n:
            found["nitpick"] += int(n.group(1))
    return found, actionable


def carried(branch, sha, pr=None):
    """Every request from <branch> whose head is <sha>, closed ones included, and <pr>: when a gate branch is deleted and
    pushed again its request closes, a new one opens on the same commit, and CodeRabbit's review stays on the first."""
    owner = REPO.split("/")[0]
    pulls = gh(f"repos/{REPO}/pulls?state=all&head={owner}:{branch}") or []
    found = {p["number"] for p in pulls if (p.get("head") or {}).get("sha", "").startswith(sha)}
    if pr is not None:
        found.add(pr)
    return sorted(found)


def read_review(numbers, sha):
    """The commit's findings from the request that holds its review: each request is counted on its own and the one with
    the most findings is the review, so a review summary mirrored onto a second request is never counted twice. None when
    any request could not be read."""
    best = None
    for number in numbers:
        read_reviews = gh(f"repos/{REPO}/pulls/{number}/reviews")
        read_comments = gh(f"repos/{REPO}/pulls/{number}/comments")
        if read_reviews is None or read_comments is None:
            return None
        reviews = [r for r in read_reviews if (r.get("user") or {}).get("login") == BOT]
        comments = [c for c in read_comments if (c.get("user") or {}).get("login") == BOT]
        found, actionable = count(reviews, comments, sha)
        here = (sum(found.values()), actionable, bool(reviews or comments), number, found)
        if best is None or here[:3] > best[:3]:
            best = here
    return best


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
    if sys.argv[1] == "count":
        with open(sys.argv[2], encoding="utf-8") as f:
            found, actionable = count([], json.load(f), sys.argv[3] if len(sys.argv) > 3 else None)
        print(json.dumps({"findings": found, "actionable": actionable}))
        return
    if sys.argv[1] == "recount":
        version, branch, sha, log_path = sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5]
        with open(log_path, encoding="utf-8") as f:
            log = json.load(f)
        row = next(r for r in log["rows"] if r.get("version") == version)
        numbers = carried(branch, sha, row.get("pr"))
        best = read_review(numbers, sha)
        if best is None:
            sys.exit(f"recount: the requests {numbers} for {version} could not be read")
        row["prs"] = numbers
        row["findings"], row["actionable"] = best[4], best[1]
        if best[2]:
            row["pr"], row["reviewed"] = best[3], "full"
        with open(log_path, "w", encoding="utf-8") as f:
            json.dump(log, f, indent=2, ensure_ascii=False)
            f.write("\n")
        print(f"recount: {version} on {numbers}, {row['findings']}, actionable {row['actionable']}")
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
        # The machine that ships opens the request right after its push; none after five minutes means it was not
        # opened, and waiting out the ceiling would only hide that.
        if pr is None and time.monotonic() - start > NO_REQUEST_AFTER:
            break
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
                if newest.get("status") == "completed" and ("skipped" not in words.lower() or "rate limit" in words.lower()):
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
        numbers = carried(branch, sha, pr)
        row["prs"] = numbers
        best = read_review(numbers, sha)
        if best is None:
            row["reviewed"] = "completed, findings not read"
        else:
            row["findings"], row["actionable"] = best[4], best[1]
            row["pr"] = best[3] if best[2] else pr
            if "rate limit" in words:
                row["reviewed"] = "rate limited"
            elif best[2] or "review completed" in words:
                row["reviewed"] = "full"
            else:
                row["reviewed"] = "summary only"
    with open(out, "w", encoding="utf-8") as f:
        json.dump(row, f, indent=2)
        f.write("\n")
    print(json.dumps(row))
    if row["reviewed"].startswith("not reviewed") or row["reviewed"].endswith("not read"):
        kind = "warning" if pr is None else "notice"
        print(f"::{kind}::CodeRabbit: {row['reviewed']}; Level 0 ships anyway (ADR-129). Open it with scripts/gate_pr.sh open <version> after the push.")


if __name__ == "__main__":
    main()
