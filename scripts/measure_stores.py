"""Measure both stores in one session, the way the search index was measured.

Paired rounds, both containers inside each round, alternating which goes
first, and the median of the per-round differences alongside each side's own
medians. Sequential timings on a shared network measure the network; pairing
measures the difference (api/TheYard.Tests/SearchIndexBenchmarkTests.cs, and
ADR: Measuring both stores).

Every round is what a visitor does: register, sign in, load a listing page,
open a vehicle, read the filter values, place a bid, raise it, and reset. Each
request is timed from this machine; the Cosmos DB side's request charge per
route is read from that container's own Admin metrics afterwards, because the
container is the only thing that knows what the store charged it.

    python scripts/measure_stores.py --sql http://... --cosmos http://... --rounds 20
"""

from __future__ import annotations

import argparse
import http.cookiejar
import json
import statistics
import sys
import time
import urllib.error
import urllib.request
import uuid
from datetime import datetime, timezone

ROUTES = [
    "register",
    "sign in",
    "listing page",
    "vehicle page",
    "filter values",
    "bid write",
    "bid raise",
    "reset",
]


class Side:
    """One container: its origin, a cookie jar per round, and the timings it produced."""

    def __init__(self, name: str, origin: str) -> None:
        self.name = name
        self.origin = origin.rstrip("/")
        self.timings: dict[str, list[float]] = {route: [] for route in ROUTES}
        self.opener = None

    def fresh_browser(self) -> None:
        jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))

    def call(self, method: str, path: str, body: dict | None = None) -> tuple[int, dict | list | None, float]:
        data = None if body is None else json.dumps(body).encode()
        request = urllib.request.Request(self.origin + path, data=data, method=method)
        if data is not None:
            request.add_header("Content-Type", "application/json")
        started = time.perf_counter()
        try:
            with self.opener.open(request, timeout=60) as response:
                elapsed = (time.perf_counter() - started) * 1000
                raw = response.read()
                status = response.status
        except urllib.error.HTTPError as error:
            elapsed = (time.perf_counter() - started) * 1000
            raw = error.read()
            status = error.code
        parsed = None
        if raw:
            try:
                parsed = json.loads(raw)
            except json.JSONDecodeError:
                parsed = None
        return status, parsed, elapsed

    def timed(self, route: str, method: str, path: str, body: dict | None = None):
        status, parsed, elapsed = self.call(method, path, body)
        self.timings[route].append(elapsed)
        return status, parsed


def one_round(side: Side, round_number: int) -> None:
    side.fresh_browser()
    email = f"measure-{round_number}-{uuid.uuid4().hex}@example.com"

    status, _ = side.timed("register", "POST", "/api/auth/register", {"email": email, "password": "correct horse"})
    assert status == 200, f"{side.name}: register answered {status}"
    status, _ = side.timed("sign in", "POST", "/api/auth/login", {"email": email, "password": "correct horse"})
    assert status == 200, f"{side.name}: sign in answered {status}"

    status, listing = side.timed("listing page", "GET", "/api/vehicles?limit=100&status=live")
    assert status == 200 and listing, f"{side.name}: listing answered {status}"
    # The listing is sorted by ending soonest, so its first rows end within
    # seconds and a bid on one of them arrives at an auction that has ended.
    # Take a live one with at least five minutes left.
    now_ms = int(time.time() * 1000)
    vehicle = next(
        v for v in listing["vehicles"]
        if v["auction_status"] == "live" and v["auction_ends_at"] - now_ms > 5 * 60 * 1000
    )
    vehicle_id = vehicle["id"]

    status, opened = side.timed("vehicle page", "GET", f"/api/vehicles/{vehicle_id}")
    assert status == 200, f"{side.name}: vehicle answered {status}"
    status, _ = side.timed("filter values", "GET", "/api/facets")
    assert status == 200, f"{side.name}: facets answered {status}"

    amount = opened["min_next_bid"]
    for attempt in range(3):
        status, answer = side.timed("bid write", "POST", f"/api/vehicles/{vehicle_id}/bids", {"amount": amount})
        if status == 200:
            break
        # Somebody else moved the price between the read and the bid; read it again.
        side.timings["bid write"].pop()
        _, opened, _ = side.call("GET", f"/api/vehicles/{vehicle_id}")
        amount = opened["min_next_bid"]
    assert status == 200, f"{side.name}: bid answered {status}: {answer}"

    raised = answer["vehicle"]["min_next_bid"]
    status, answer = side.timed("bid raise", "POST", f"/api/vehicles/{vehicle_id}/bids", {"amount": raised})
    assert status == 200, f"{side.name}: raise answered {status}: {answer}"

    status, _ = side.timed("reset", "DELETE", "/api/bids")
    assert status == 204, f"{side.name}: reset answered {status}"


def percentile(values: list[float], p: int) -> float:
    ordered = sorted(values)
    rank = max(0, min(len(ordered) - 1, int(-(-p * len(ordered) // 100)) - 1))
    return ordered[rank]


def metrics(side: Side) -> dict:
    side.fresh_browser()
    status, parsed, _ = side.call("GET", "/api/admin/metrics")
    return parsed if status == 200 and isinstance(parsed, dict) else {}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--sql", required=True, help="origin of the container on Azure SQL Database")
    parser.add_argument("--cosmos", required=True, help="origin of the container on Azure Cosmos DB")
    parser.add_argument("--rounds", type=int, default=20)
    parser.add_argument("--out", default="measure-stores.json")
    args = parser.parse_args()

    sql = Side("Azure SQL Database", args.sql)
    cosmos = Side("Azure Cosmos DB", args.cosmos)
    before = {"sql": metrics(sql), "cosmos": metrics(cosmos)}

    for round_number in range(args.rounds):
        order = [sql, cosmos] if round_number % 2 == 0 else [cosmos, sql]
        for side in order:
            one_round(side, round_number)
        print(f"round {round_number + 1} of {args.rounds} done", file=sys.stderr)

    after = {"sql": metrics(sql), "cosmos": metrics(cosmos)}

    rows = []
    for route in ROUTES:
        a, b = sql.timings[route], cosmos.timings[route]
        diffs = [y - x for x, y in zip(a, b)]
        rows.append(
            {
                "route": route,
                "sql_p50_ms": round(statistics.median(a), 1),
                "sql_p95_ms": round(percentile(a, 95), 1),
                "cosmos_p50_ms": round(statistics.median(b), 1),
                "cosmos_p95_ms": round(percentile(b, 95), 1),
                "median_difference_ms": round(statistics.median(diffs), 1),
            }
        )

    charges = {entry["route"]: entry for entry in after["cosmos"].get("store_by_route", [])}
    result = {
        "measured_at": datetime.now(timezone.utc).isoformat(),
        "rounds": args.rounds,
        "sql": {"origin": sql.origin, "startup": after["sql"].get("startup")},
        "cosmos": {"origin": cosmos.origin, "startup": after["cosmos"].get("startup"), "store": after["cosmos"].get("store"), "charges_by_route": charges},
        "rows": rows,
        "before": {"sql_window": before["sql"].get("requests", {}).get("window"), "cosmos_window": before["cosmos"].get("requests", {}).get("window")},
    }
    with open(args.out, "w", encoding="utf-8") as handle:
        json.dump(result, handle, indent=2)

    print(f"\n{args.rounds} paired rounds, milliseconds, from {sys.platform}\n")
    print("| what a visitor does | SQL p50 | SQL p95 | Cosmos p50 | Cosmos p95 | median difference (Cosmos minus SQL) |")
    print("| --- | --- | --- | --- | --- | --- |")
    for row in rows:
        print(f"| {row['route']} | {row['sql_p50_ms']} | {row['sql_p95_ms']} | {row['cosmos_p50_ms']} | {row['cosmos_p95_ms']} | {row['median_difference_ms']:+} |")
    print("\nCosmos DB request charge by route (this container's own store log, median per request):")
    for route, entry in sorted(charges.items()):
        print(f"  {route}: {entry['ru_p50']} RU over {entry['operations_per_request']} operations, max {entry['ru_max']} RU, {entry['cross_partition']} cross-partition")
    for name, side in (("sql", sql), ("cosmos", cosmos)):
        startup = after[name].get("startup") or {}
        print(f"\n{side.name} startup: ready {startup.get('ready_ms')} ms, store check {startup.get('schema_ms')} ms, seed {startup.get('seed_ms')} ms / {startup.get('seed_ru')} RU, catalogue {startup.get('catalogue_ms')} ms, bids {startup.get('bids_ms')} ms")
    return 0


if __name__ == "__main__":
    sys.exit(main())
