"""admin_probe.py [--out <file>] [--warm] [--sites first|both] | --summary <file> [--since <iso>]. Reads every Admin card route
on both names, read only, and writes one JSON line a read: the name, the route, the status, the seconds, the card's
read age, tone, cadence and unread line, and the text of every dial, row and readout line whose tone is not a resting
one. With --warm it reads each route a second time five seconds after the first, so a cold read (the first in a quiet
cache minute) and a warm one are both measured. --summary prints per route the count by status, p50, p95 and max for
the cold and the warm reads, and how many reads came back unread. Stdlib only, so it runs on the box with no install
and as a hidden user timer for the soak (ADR: One frame for every resource, the addendum on the cadence)."""
import argparse, json, statistics, sys, time, urllib.error, urllib.request, datetime

SITES = {"theyard": "https://theyard.stevenstout.biz", "theyard-cosmos": "https://theyard-cosmos.stevenstout.biz"}
CARDS = ["sqldb", "cosmosdb", "registry", "insights", "share", "blob", "mail", "edge", "nightly", "claude", "campaign", "network"]
ROUTES = [f"/api/admin/cloud/{k}" for k in CARDS] + ["/api/admin/plan", "/api/admin/machines", "/api/admin/azure", "/api/admin/follows"]
RESTING = {None, "", "good", "fine", "info", "ok"}


def get(url):
    """One GET with the cache bypassed; gives the status, the body and the seconds, and never raises."""
    req = urllib.request.Request(url, headers={"Cache-Control": "no-cache", "User-Agent": "TheYard-SelfRead/1 (admin-probe)"})
    t0 = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return r.status, r.read().decode("utf-8", "replace"), round(time.perf_counter() - t0, 3)
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace"), round(time.perf_counter() - t0, 3)
    except Exception as e:
        return 0, type(e).__name__, round(time.perf_counter() - t0, 3)


def flagged(card):
    """Every dial, row and readout line whose tone is not resting, as one short text each."""
    out = []
    for d in card.get("dials") or []:
        if d.get("tone") not in RESTING:
            out.append(f"dial {d.get('name')}: {d.get('big')} {d.get('absolute') or ''} ({d.get('tone')})".strip())
    for r in card.get("rows") or []:
        if r.get("tone") not in RESTING:
            out.append(f"row {r.get('question')}: {r.get('answer')} ({r.get('tone')})")
    for line in (card.get("readout") or {}).get("lines") or []:
        if line.get("tone") not in RESTING:
            out.append(f"line {line.get('label')}: {line.get('value')} ({line.get('tone')})")
    return out


def read_once(name, site, route, kind):
    """Reads one route and gives its JSON line."""
    status, body, seconds = get(site + route)
    line = {"at": datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds"), "name": name,
            "route": route, "kind": kind, "status": status, "seconds": seconds}
    try:
        card = json.loads(body)
    except ValueError:
        line["body"] = body[:120]
        return line
    if isinstance(card, dict):
        for key in ("read_age_seconds", "tone", "cadence_seconds", "unread_after_seconds", "available"):
            if key in card:
                line[key] = card[key]
        line["flagged"] = flagged(card)
    return line


def probe(args):
    """One pass over every route on the chosen names, cold then (with --warm) warm."""
    names = ["theyard"] if args.sites == "first" else list(SITES)
    lines = []
    for name in names:
        for route in ROUTES:
            lines.append(read_once(name, SITES[name], route, "cold"))
    if args.warm:
        time.sleep(5)
        for name in names:
            for route in ROUTES:
                lines.append(read_once(name, SITES[name], route, "warm"))
    with open(args.out, "a", encoding="utf-8") if args.out else sys.stdout as f:
        for line in lines:
            f.write(json.dumps(line) + "\n")
    return 0


def pct(values, p):
    """The p-th percentile by nearest rank, or None for no values."""
    if not values:
        return None
    ordered = sorted(values)
    return ordered[max(0, min(len(ordered) - 1, round(p / 100 * len(ordered) + 0.5) - 1))]


def summary(args):
    """Per name and route: statuses, cold and warm p50 p95 max, unread count, and the flagged texts seen."""
    groups = {}
    with open(args.summary, encoding="utf-8") as f:
        for raw in f:
            try:
                line = json.loads(raw)
            except ValueError:
                continue
            if args.since and line.get("at", "") < args.since:
                continue
            g = groups.setdefault((line["name"], line["route"]), {"status": {}, "cold": [], "warm": [], "unread": 0, "flags": {}})
            g["status"][line["status"]] = g["status"].get(line["status"], 0) + 1
            g[line.get("kind", "cold")].append(line["seconds"])
            if line.get("tone") == "unread":
                g["unread"] += 1
            for text in line.get("flagged") or []:
                g["flags"][text.split(":")[0]] = g["flags"].get(text.split(":")[0], 0) + 1
    print(f"{'name':15} {'route':30} {'statuses':14} {'cold p50/p95/max':22} {'warm p50/p95/max':22} unread")
    for (name, route), g in sorted(groups.items()):
        cold = "/".join(str(pct(g["cold"], p)) for p in (50, 95, 100))
        warm = "/".join(str(pct(g["warm"], p)) for p in (50, 95, 100))
        statuses = ",".join(f"{k}x{v}" for k, v in sorted(g["status"].items()))
        print(f"{name:15} {route:30} {statuses:14} {cold:22} {warm:22} {g['unread']}")
        for text, n in sorted(g["flags"].items()):
            print(f"{'':15}   flagged {n}x {text}")
    return 0


def main():
    """Parses the flags and runs a probe pass or a summary."""
    parser = argparse.ArgumentParser(description=__doc__.split(".")[0])
    parser.add_argument("--out")
    parser.add_argument("--warm", action="store_true")
    parser.add_argument("--sites", choices=["first", "both"], default="both")
    parser.add_argument("--summary")
    parser.add_argument("--since")
    args = parser.parse_args()
    return summary(args) if args.summary else probe(args)


if __name__ == "__main__":
    sys.exit(main())
