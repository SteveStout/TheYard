"""admin_after_roll.py <version> <commit> <log>. The reads after a roll, both sites, read only (Steve: "make sure you do
some health checks on the admin section after deploy"). Waits up to twenty-five minutes for both names to answer the
version and commit on /api/version, then reads /api/health (every check named), /api/admin/pages (every address not
up, named), the Admin routes in READS and the tests summary, each with its status and time. When the roll's sweep has
anything down, waits for the settled sweep (three minutes after the start) and reads pages again; if that still has
anything down, asks for one more sweep and reads once more. Prints one line per site that starts ADMIN HEALTH, and
"ADMIN HEALTH AFTER THE ROLL: fine" or "... NOT FINE" last, and exits 1 when not fine. The gate's after-roll job runs
it (ADR: The gate runs where the memory is, the addendum of 1.0.4.101)."""
import sys, json, time, urllib.request, urllib.error, datetime
version = sys.argv[1]; commit = sys.argv[2][:7]; log = sys.argv[3]
SITES = ["https://theyard.stevenstout.biz", "https://theyard-cosmos.stevenstout.biz"]
READS = ["/api/admin/machines", "/api/admin/kept?card=errors&window=24h", "/api/admin/metrics", "/api/admin/telemetry", "/api/admin/azure", "/api/admin/activity?window=24h", "/api/tests/summary"]
def say(m):
    print(m, flush=True)
    with open(log, "a", encoding="utf-8") as f: f.write(m + "\n")
def get(url, method="GET"):
    sep = "&" if "?" in url else "?"
    req = urllib.request.Request(url + f"{sep}nocache={int(time.time()*1000)}", method=method, headers={"Cache-Control": "no-cache", "User-Agent": "TheYard-SelfRead/1 (admin-after-roll)"})
    t0 = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=90) as r:
            body = r.read().decode("utf-8", "replace"); return r.status, body, int((time.perf_counter() - t0) * 1000)
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace"), int((time.perf_counter() - t0) * 1000)
    except Exception as e:
        return 0, type(e).__name__, int((time.perf_counter() - t0) * 1000)
def pages(site):
    status, body, ms = get(site + "/api/admin/pages")
    try:
        j = json.loads(body); r = j.get("report") or {}
        down = [e for e in r.get("entries", []) if not e.get("ok")]
        return j.get("status"), r.get("checked"), r.get("up"), r.get("trigger"), r.get("at"), down, ms
    except Exception:
        return f"unreadable {status}", None, None, None, None, [], ms
fine = True
say(f"### admin health after the roll onto {version}, started {datetime.datetime.now():%Y-%m-%d %H:%M:%S}")
# The roll: both names answer the version and its commit, or the reads below would read the version before it.
deadline = time.time() + 1500
live = {}
while time.time() < deadline:
    for site in SITES:
        status, body, ms = get(site + "/api/version")
        live[site] = version in body and commit in body
    if all(live.values()): break
    time.sleep(20)
say(f"  both names on {version} ({commit}): {all(live.values())} {live}")
if not all(live.values()):
    say("ADMIN HEALTH AFTER THE ROLL: NOT FINE (the roll was not read on both names)")
    sys.exit(1)
for site in SITES:
    name = site.split("//")[1].split(".")[0]
    problems = []
    status, body, ms = get(site + "/api/version")
    if version not in body: problems.append(f"version answered {status} {body[:60]}")
    status, body, ms = get(site + "/api/health")
    try:
        h = json.loads(body); failing = [c["name"] for c in h["checks"] if c["status"] != "pass"]
        say(f"  {name} health: {h['status']} up {h['uptime_seconds']} s in {ms} ms, failing {failing}")
        if h["status"] != "healthy": problems.append(f"health {h['status']}, failing {failing}")
    except Exception:
        problems.append(f"health answered {status} in {ms} ms: {body[:80]}"); say(f"  {name} health: {status} {body[:80]}")
    for path in READS:
        status, body, ms = get(site + path)
        say(f"  {name} {path}: {status} in {ms} ms, {len(body)} chars")
        if status != 200: problems.append(f"{path} answered {status} in {ms} ms")
    st, checked, up, trigger, at, down, ms = pages(site)
    say(f"  {name} pages: {st}, {up} of {checked} up, trigger {trigger} at {at}, down {[d['address'] + ' ' + str(d.get('reason') or d.get('status')) for d in down]}")
    if down:
        # The settled sweep runs three minutes after the start; wait for it, then read again.
        say(f"  {name}: waiting for the settled sweep")
        for _ in range(24):
            time.sleep(15)
            st, checked, up, trigger, at, down, ms = pages(site)
            if trigger == "settled" and st == "done": break
        say(f"  {name} pages after the settled sweep: {st}, {up} of {checked} up, trigger {trigger}, down {[d['address'] + ' ' + str(d.get('reason') or d.get('status')) for d in down]}")
        if down:
            status, body, ms = get(site + "/api/admin/pages", "POST")
            say(f"  {name}: asked for one more sweep -> {status}")
            for _ in range(12):
                time.sleep(10)
                st, checked, up, trigger, at, down, ms = pages(site)
                if trigger == "asked" and st == "done": break
            say(f"  {name} pages after the asked sweep: {st}, {up} of {checked} up, down {[d['address'] + ' ' + str(d.get('reason') or d.get('status')) for d in down]}")
        if down: problems.append(f"pages {up} of {checked} up after the settled sweep")
    say(f"ADMIN HEALTH {name}: {'fine' if not problems else 'NOT FINE: ' + '; '.join(problems)}")
    if problems: fine = False
say(f"ADMIN HEALTH AFTER THE ROLL: {'fine' if fine else 'NOT FINE'}")
sys.exit(0 if fine else 1)
