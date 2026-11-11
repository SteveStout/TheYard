"""coderabbit_review.py <branch> <sha> <ceiling-seconds> <out.json>
                     | fold <row.json> <log.json>
                     | count <comments.json> [<sha> [<reviews.json>]]
                     | missing <version> <sha> <out.json>
                     | recount <version> <branch> <sha> <log.json>
                     | dispose <version> <fixed> <answered-as-wrong> <log.json>
Waits for CodeRabbit's review of the gate's pull request and writes one row of the findings log (ADR-129). Finds the open pull request from <branch>, then waits for the
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
# Five minutes with no request means none was opened; a read every 20 seconds. Both can be shortened by the
# environment, which only the tests that run the wait for real do.
NO_REQUEST_AFTER = int(os.environ.get("CODERABBIT_NO_REQUEST_AFTER", "300"))
POLL_SECONDS = float(os.environ.get("CODERABBIT_POLL_SECONDS", "20"))


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


OUTSIDE = re.compile(r"Outside diff range comments \((\d+)\)")
# A whole severity header, in the current form or the older italic one; a bold title holding a severity word is neither.
HEADER = re.compile(
    r"^(?:\*\*<picture>.*?</picture>\s*(Critical|Major|Minor|Trivial)\*\*|_[^_\n]*_\s*\|\s*_(Critical|Major|Minor|Trivial)_)"
)


def outside(body):
    """The findings CodeRabbit puts in the review body because GitHub cannot post them on a line outside the diff,
    each by the label in its own header, and any the section counts but no header names as nitpicks. A header is a
    quoted line that is a whole severity header, current or older, so a remark or a title that holds a severity word
    is never read as one."""
    m = OUTSIDE.search(body)
    if not m:
        return []
    quoted = []
    for line in body[m.end():].splitlines()[1:]:
        if not line.startswith(">"):
            break
        quoted.append(line[1:].strip())
    found = (HEADER.match(line) for line in quoted)
    labels = [(h.group(1) or h.group(2)).lower() for h in found if h][: int(m.group(1))]
    return labels + ["nitpick"] * max(0, int(m.group(1)) - len(labels))


ITEM = re.compile(r"^`[^`]+`: \*\*<picture>.*?</picture>\s*(Critical|Major|Minor|Trivial)\*\*", re.M)
SECTION_END = re.compile(r"Additional comments|Prompt to fix all|Review info|Outside diff range|Duplicate comments")


def nitpicks(body):
    """The nitpick section of a review body: in the older body only its count, "Nitpick comments (N)", which are all
    nitpicks; in the newer, "Nitpick comments: N", each item under its own header label, and any the count names
    that no header does as nitpicks."""
    older = re.search(r"Nitpick comments \((\d+)\)", body)
    if older:
        return ["nitpick"] * int(older.group(1))
    newer = re.search(r"Nitpick comments:\s*(\d+)", body)
    if not newer:
        return []
    rest = body[newer.end():]
    end = SECTION_END.search(rest)
    labels = [m.group(1).lower() for m in ITEM.finditer(rest[: end.start()] if end else rest)][: int(newer.group(1))]
    return labels + ["nitpick"] * (int(newer.group(1)) - len(labels))


def count(reviews, comments, sha=None):
    """Findings by severity on one commit: each inline comment that opens a thread once (a reply in a thread is not
    a finding), the findings a review body carries from outside the diff, and the nitpicks a review body folds into
    its own count, in either of the two ways CodeRabbit has written that count. With <sha>, only that commit's review
    and comments count, so a second push to the same pull request does not count the first commit's findings again.
    Each Critical on a line is kept with its file and line, so a hold can say where it is."""
    found = {name: 0 for name in SEVERITIES}
    places = []
    for c in comments:
        if c.get("in_reply_to_id") or (sha and c.get("commit_id") not in (None, sha)):
            continue
        label = severity(c.get("body") or "")
        found[label] += 1
        if label == "critical" and c.get("path"):
            places.append({"path": c["path"], "line": c.get("line") or c.get("original_line")})
    actionable = 0
    for r in reviews:
        if sha and r.get("commit_id") not in (None, sha):
            continue
        body = r.get("body") or ""
        m = re.search(r"Actionable comments posted:\s*(\d+)", body)
        if m:
            actionable += int(m.group(1))
        for label in nitpicks(body) + outside(body):
            found[label] += 1
    return found, actionable, places


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
    held = (0, [])
    for number in numbers:
        read_reviews = gh(f"repos/{REPO}/pulls/{number}/reviews")
        read_comments = gh(f"repos/{REPO}/pulls/{number}/comments")
        if read_reviews is None or read_comments is None:
            return None
        reviews = [r for r in read_reviews if (r.get("user") or {}).get("login") == BOT]
        comments = [c for c in read_comments if (c.get("user") or {}).get("login") == BOT]
        found, actionable, places = count(reviews, comments, sha)
        # Each Critical names the request it was found on, which a carried Critical's row number would not.
        places = [{**place, "pr": number} for place in places]
        # A Critical in the review body has no line; it still names the request it was found on.
        places += [{"path": "the review body", "line": None, "pr": number}] * max(0, found["critical"] - len(places))
        here = (sum(found.values()), actionable, bool(reviews or comments), number, found, places)
        if best is None or here[:3] > best[:3]:
            best = here
        if found["critical"] > held[0]:
            held = (found["critical"], places)
    # The count comes from one request, but a Critical another request found for the same commit is never dropped.
    if best is not None and held[0] > best[4]["critical"]:
        best = best[:4] + ({**best[4], "critical": held[0]}, held[1])
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
        "critical_at": [],
        # What became of the findings, read from the request's threads and the next versions once they are answered;
        # null until then, never a guess (ADR-129, the addendum of 1.0.5.7).
        "fixed": None,
        "answered_as_wrong": None,
    }


def unread(row):
    """A row whose read found nothing to count: no review, findings not read, or a rate-limited review with no findings
    and no actionable comments."""
    said = row.get("reviewed") or ""
    empty = sum((row.get("findings") or {}).values()) == 0 and not row.get("actionable")
    return said.startswith("not reviewed") or said.endswith("not read") or (said == "rate limited" and empty)


def fold(row_path, log_path):
    """One ship's row into the findings log, newest last; a re-run of a version replaces its row, except that a re-run
    which could not read a review never replaces one that was read, and what became of the findings is kept with the
    findings it counts. Outcomes that no longer fit the row's findings are set back to null, to be read again."""
    with open(row_path, encoding="utf-8") as f:
        row = json.load(f)
    log = {"about": "One row per ship: CodeRabbit's review of the gate's pull request, the evidence for every rung of the ladder (ADR-129).", "rows": []}
    if os.path.exists(log_path):
        with open(log_path, encoding="utf-8") as f:
            log = json.load(f)
    kept = next((r for r in log["rows"] if r.get("version") == row.get("version")), None)
    if kept is not None and unread(row) and not unread(kept):
        print(f"findings log: {row.get('version')} keeps its read review; this read says {row.get('reviewed')}")
        return
    for key in ("fixed", "answered_as_wrong"):
        if kept is not None and row.get(key) is None and kept.get(key) is not None:
            row[key] = kept[key]
    if (row.get("fixed") or 0) + (row.get("answered_as_wrong") or 0) > sum(row["findings"].values()):
        print(f"::warning::findings log: {row.get('version')}'s outcomes no longer fit its findings; set back to null to be read again")
        row["fixed"] = row["answered_as_wrong"] = None
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
            comments = json.load(f)
        reviews = []
        if len(sys.argv) > 4:
            with open(sys.argv[4], encoding="utf-8") as f:
                reviews = json.load(f)
        found, actionable, places = count(reviews, comments, sys.argv[3] if len(sys.argv) > 3 else None)
        print(json.dumps({"findings": found, "actionable": actionable, "critical_at": places}))
        return
    if sys.argv[1] == "dispose":
        if len(sys.argv) != 6:
            sys.exit("usage: coderabbit_review.py dispose <version> <fixed> <answered-as-wrong> <log.json>")
        version, fixed, wrong, log_path = sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), sys.argv[5]
        with open(log_path, encoding="utf-8") as f:
            log = json.load(f)
        row = next((r for r in log["rows"] if r.get("version") == version), None)
        if row is None:
            sys.exit(f"dispose: {log_path} has no row for {version}")
        total = sum(row["findings"].values())
        if fixed < 0 or wrong < 0 or fixed + wrong > total:
            sys.exit(f"dispose: {version} has {total} findings, so {fixed} fixed and {wrong} answered as wrong cannot be")
        row["fixed"], row["answered_as_wrong"] = fixed, wrong
        with open(log_path, "w", encoding="utf-8") as f:
            json.dump(log, f, indent=2, ensure_ascii=False)
            f.write("\n")
        print(f"dispose: {version}, {fixed} fixed and {wrong} answered as wrong of {total}")
        return
    if sys.argv[1] == "recount":
        if len(sys.argv) != 6:
            sys.exit("usage: coderabbit_review.py recount <version> <branch> <sha> <log.json>")
        version, branch, sha, log_path = sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5]
        with open(log_path, encoding="utf-8") as f:
            log = json.load(f)
        row = next((r for r in log["rows"] if r.get("version") == version), None)
        if row is None:
            sys.exit(f"recount: {log_path} has no row for {version}")
        numbers = carried(branch, sha, row.get("pr"))
        best = read_review(numbers, sha)
        if best is None:
            sys.exit(f"recount: the requests {numbers} for {version} could not be read")
        decided = (row.get("fixed") or 0) + (row.get("answered_as_wrong") or 0)
        if decided > sum(best[4].values()):
            sys.exit(f"recount: {version} now reads {sum(best[4].values())} findings and holds {decided} outcomes; set them again with dispose first")
        row["prs"] = numbers
        row["findings"], row["actionable"], row["critical_at"] = best[4], best[1], best[5]
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
        time.sleep(POLL_SECONDS)
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
            row["findings"], row["actionable"], row["critical_at"] = best[4], best[1], best[5]
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
