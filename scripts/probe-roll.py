"""What a visitor sees during a roll, read every two seconds from wherever this runs (ADR: Blue-green on a $13 plan,
measured and held). Read only. For each site, the origin's page (/), /healthz, /api/version and /api/facets, and the
domain's /api/version, each with its status, its time and the version that answered; a read is given up at 30 seconds.
One line a read, then a summary: per address, the longest stretch with no good answer, the slowest good answer, and when
each version first answered. Independent of Application Insights, which stops recording at its daily cap.

    python scripts/probe-roll.py <log path> [seconds, default 900]

Started just before a push, it covers the roll that push sets off; the gate on the build machine starts it for 1500
seconds."""
import sys, time, json, datetime, threading, urllib.request

LOG = sys.argv[1]
seconds = int(sys.argv[2]) if len(sys.argv) > 2 else 900
SITES = {
    "sql": ("https://app-theyard-ss-zmnetj67bn5h2.azurewebsites.net", "https://theyard.stevenstout.biz"),
    "cosmos": ("https://app-theyard-cosmos-ss-zmnetj67bn5h2.azurewebsites.net", "https://theyard-cosmos.stevenstout.biz"),
}
TARGETS = []
for site, (origin, domain) in SITES.items():
    TARGETS += [(f"{site} origin /", origin + "/"), (f"{site} origin /healthz", origin + "/healthz"), (f"{site} origin /api/version", origin + "/api/version"),
                (f"{site} origin /api/facets", origin + "/api/facets"), (f"{site} domain /api/version", domain + "/api/version")]
lock = threading.Lock()
rows = []

def read(name, url):
    t0 = time.time()
    status, body = 0, ""
    try:
        req = urllib.request.Request(url + ("&" if "?" in url else "?") + f"probe={int(t0*1000)}",
                                     headers={"User-Agent": "TheYard-SelfRead/1 (roll probe)", "Cache-Control": "no-cache"})
        with urllib.request.urlopen(req, timeout=30) as r:
            status, body = r.status, r.read(400).decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        status = e.code
    except Exception as e:
        status, body = -1, type(e).__name__
    ms = int((time.time() - t0) * 1000)
    version = ""
    if "version" in name and status == 200:
        try:
            version = json.loads(body).get("version", "")
        except Exception:
            pass
    with lock:
        rows.append((t0, name, status, ms, version))
        with open(LOG, "a", encoding="utf-8") as f:
            f.write(f"{datetime.datetime.fromtimestamp(t0).strftime('%H:%M:%S.%f')[:-3]}\t{name}\t{status}\t{ms}\t{version}\n")

open(LOG, "w", encoding="utf-8").write(f"### probe, {seconds} s, started {datetime.datetime.now():%Y-%m-%d %H:%M:%S}\n")
end = time.time() + seconds
while time.time() < end:
    for name, url in TARGETS:
        threading.Thread(target=read, args=(name, url), daemon=True).start()
    time.sleep(2)
time.sleep(31)
with open(LOG, "a", encoding="utf-8") as f:
    f.write("### SUMMARY\n")
    for name, _ in TARGETS:
        mine = sorted((r for r in rows if r[1] == name), key=lambda r: r[0])
        good = [r for r in mine if r[2] == 200]
        worst_gap, last_good = 0.0, mine[0][0] if mine else 0
        for r in mine:
            if r[2] == 200:
                worst_gap = max(worst_gap, r[0] - last_good)
                last_good = r[0] + r[3] / 1000
        firsts = {}
        for r in good:
            if r[4] and r[4] not in firsts:
                firsts[r[4]] = datetime.datetime.fromtimestamp(r[0]).strftime("%H:%M:%S")
        slow = max((r[3] for r in good), default=0)
        f.write(f"  {name}: {len(mine)} reads, {len(good)} good, longest stretch with no good answer {worst_gap:.0f} s, slowest good {slow} ms, versions first seen {firsts}\n")
    f.write(f"### DONE {datetime.datetime.now():%H:%M:%S}\n")
