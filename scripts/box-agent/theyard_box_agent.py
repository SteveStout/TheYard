#!/usr/bin/env python3
"""The box reports itself (ADR: The box reports itself).

Reads what Linux's System Monitor shows, from /proc, /sys, df, nvidia-smi and systemctl, and posts it to the
site: a small live sample every two seconds and the full snapshot every thirtieth tick, on one kept-open HTTPS
connection, behind the operator's key read from a file of mode 600. Standard library only; no sudo.

  --dry-run  print one snapshot and one sample as JSON, post nothing, never print the key
  --once     post one snapshot and one sample, print the two statuses
"""
import datetime, http.client, json, os, signal, socket, subprocess, sys, time, urllib.parse

VERSION = "1"
TICK, SNAPSHOT_EVERY, KEEP_SNAPSHOTS = 2.0, 30, 60
BACKOFF = [2, 4, 8, 16, 32, 60]
CONFIG = os.path.expanduser("~/.config/theyard")
USER_UNITS = ["claude-remote-control.service", "jdrive.service", "theyard-box-agent.service",
              "app-dev.lizardbyte.app.Sunshine.service", "jobhunt-6am.timer", "jobhunt-6am.service"]
SYSTEM_UNITS = ["tailscaled.service", "ssh.service", "xrdp.service", "unattended-upgrades.service", "docker.service"]
REAL_FS = {"ext4", "ext3", "xfs", "btrfs", "vfat", "ntfs", "exfat", "f2fs", "zfs"}

def now_iso():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()

def run(*args, timeout=5):
    """A command's output, or an empty string when it fails or is missing."""
    try:
        return subprocess.run(args, capture_output=True, text=True, timeout=timeout).stdout
    except (OSError, subprocess.SubprocessError):
        return ""

def read(path):
    try:
        with open(path) as handle:
            return handle.read()
    except OSError:
        return ""

def cpu_times():
    """Each core's busy and total jiffies, from /proc/stat."""
    times = []
    for line in read("/proc/stat").splitlines():
        if line.startswith("cpu") and line[3:4].isdigit():
            values = [int(v) for v in line.split()[1:]]
            idle = values[3] + (values[4] if len(values) > 4 else 0)
            times.append((sum(values) - idle, sum(values)))
    return times

def core_loads(before, after):
    return [round(100.0 * (b2 - b1) / (t2 - t1), 1) if t2 > t1 else 0.0 for (b1, t1), (b2, t2) in zip(before, after)]

def meminfo():
    info = {}
    for line in read("/proc/meminfo").splitlines():
        name, _, rest = line.partition(":")
        info[name] = int(rest.split()[0]) * 1024 if rest.split() else 0
    total, free = info.get("MemTotal", 0), info.get("MemFree", 0)
    cache = info.get("Buffers", 0) + info.get("Cached", 0) + info.get("SReclaimable", 0)
    used = max(0, total - free - cache)
    return ({"total_b": total, "used_b": used, "cache_b": cache, "free_b": free},
            {"total_b": info.get("SwapTotal", 0), "used_b": max(0, info.get("SwapTotal", 0) - info.get("SwapFree", 0))})

def net_totals():
    totals = {}
    for line in read("/proc/net/dev").splitlines()[2:]:
        name, _, rest = line.partition(":")
        fields = rest.split()
        if len(fields) >= 9:
            totals[name.strip()] = (int(fields[0]), int(fields[8]))
    return totals

def net_rates(before, after, seconds):
    rates = []
    for name, (rx, tx) in after.items():
        rx0, tx0 = before.get(name, (rx, tx))
        rates.append({"name": name, "in_bps": max(0.0, (rx - rx0) / seconds), "out_bps": max(0.0, (tx - tx0) / seconds),
                      "in_total_b": rx, "out_total_b": tx})
    return rates

def core_mhz():
    return [float(line.split(":")[1]) for line in read("/proc/cpuinfo").splitlines() if line.startswith("cpu MHz")]

def mdstat():
    """Each array's state and members, from /proc/mdstat: {md: (state, members, active)}; a member marked (F) is not active."""
    arrays = {}
    for line in read("/proc/mdstat").splitlines():
        if line.startswith("md") and " : " in line:
            name, _, rest = line.partition(" : ")
            words = rest.split()
            members = [w for w in words if "[" in w and w.endswith("]") or w.endswith("(F)")]
            state = " ".join(w for w in words[:2] if "[" not in w) + (" (F)" if "(F)" in rest else "")
            arrays[name.strip()] = (state, len(members), len([m for m in members if not m.endswith("(F)")]))
    return arrays

def disks():
    arrays, found, sudo = mdstat(), [], "" if run("mdadm", "--detail", "/dev/md126") else ", mdadm needs sudo"
    for line in run("df", "-B1", "--output=target,source,fstype,size,used").splitlines()[1:]:
        parts = line.split()
        if len(parts) != 5 or parts[2] not in REAL_FS:
            continue
        device = os.path.basename(parts[1])
        array = next((name for name in arrays if device.startswith(name)), None)
        state, members, active = arrays[array] if array else (None, None, None)
        found.append({"mount": parts[0], "device": device, "fstype": parts[2], "size_b": int(parts[3]), "used_b": int(parts[4]),
                      "raid_state": state + sudo if state and parts[0] == "/" else state, "members": members, "members_active": active})
    return found

def gpu_marks():
    """Each GPU's slowdown and shutdown marks, as the driver states them."""
    marks, index = [], -1
    for line in run("nvidia-smi", "-q", "-d", "TEMPERATURE").splitlines():
        if line.startswith("GPU "):
            index += 1
            marks.append({})
        elif ":" in line and index >= 0:
            name, _, value = line.partition(":")
            if "Slowdown Temp" in name or "Shutdown Temp" in name:
                digits = value.split()[0] if value.split() else ""
                marks[index]["slowdown" if "Slowdown" in name else "shutdown"] = float(digits) if digits.replace(".", "").isdigit() else None
    return marks

def gpus(marks):
    query = "index,name,utilization.gpu,memory.used,memory.total,temperature.gpu,power.draw,power.limit,pstate"
    found = []
    for line in run("nvidia-smi", f"--query-gpu={query}", "--format=csv,noheader,nounits").splitlines():
        f = [x.strip() for x in line.split(",")]
        if len(f) != 9:
            continue
        num = lambda v: float(v) if v.replace(".", "").isdigit() else None
        i = int(f[0])
        mark = marks[i] if i < len(marks) else {}
        found.append({"index": i, "name": f[1], "load": num(f[2]) or 0.0, "mem_used_b": int(float(f[3]) * 1048576),
                      "mem_total_b": int(float(f[4]) * 1048576), "temp": num(f[5]) or 0.0, "slowdown_temp": mark.get("slowdown"),
                      "shutdown_temp": mark.get("shutdown"), "power_w": num(f[6]), "power_limit_w": num(f[7]), "pstate": f[8]})
    return found

def temps(gpu_list):
    found = []
    for hw in sorted(os.listdir("/sys/class/hwmon")) if os.path.isdir("/sys/class/hwmon") else []:
        base = f"/sys/class/hwmon/{hw}"
        chip = read(f"{base}/name").strip()
        for entry in sorted(os.listdir(base)):
            if entry.startswith("temp") and entry.endswith("_input"):
                stem = entry[:-6]
                value = lambda suffix: int(read(f"{base}/{stem}_{suffix}").strip()) / 1000 if read(f"{base}/{stem}_{suffix}").strip().lstrip("-").isdigit() else None
                label = read(f"{base}/{stem}_label").strip() or stem
                if value("input") is not None:
                    found.append({"sensor": f"{chip} {label}", "now": value("input"), "high": value("max"), "critical": value("crit")})
    for gpu in gpu_list:
        found.append({"sensor": f"GPU {gpu['index']}", "now": gpu["temp"], "high": gpu["slowdown_temp"], "critical": gpu["shutdown_temp"]})
    return found[:64]

def services():
    found = []
    for scope, units in (("user", USER_UNITS), ("system", SYSTEM_UNITS)):
        flags = ["--user"] if scope == "user" else []
        for unit in units:
            props = dict(l.split("=", 1) for l in run("systemctl", *flags, "show", unit, "-p", "ActiveState,SubState,ActiveEnterTimestamp,Result,NextElapseUSecRealtime").splitlines() if "=" in l)
            since = props.get("ActiveEnterTimestamp", "")
            try:
                since_iso = datetime.datetime.strptime(" ".join(since.split()[1:3]), "%Y-%m-%d %H:%M:%S").astimezone().isoformat() if since else None
            except ValueError:
                since_iso = None
            note = f"next {props['NextElapseUSecRealtime']}" if unit.endswith(".timer") and props.get("NextElapseUSecRealtime") else None
            if unit == "jobhunt-6am.service" and props.get("Result"):
                note = f"last result {props['Result']}"
            found.append({"unit": unit, "scope": scope, "active_state": props.get("ActiveState", "unknown"),
                          "sub_state": props.get("SubState", "unknown"), "since": since_iso, "note": note})
    return found

def os_name():
    for line in read("/etc/os-release").splitlines():
        if line.startswith("PRETTY_NAME="):
            return line.split("=", 1)[1].strip('"')
    return "Linux"

class Reader:
    """The readings, each tick against the last."""

    def __init__(self):
        self.cpu, self.net, self.at = cpu_times(), net_totals(), time.monotonic()
        self.marks, self.own = gpu_marks(), (time.monotonic(), self.self_cpu())
        started = time.monotonic()
        run("nvidia-smi", "--query-gpu=index", "--format=csv,noheader")
        self.gpu_ms = round((time.monotonic() - started) * 1000)
        self.gpu_every, self.tick, self.gpus = (5 if self.gpu_ms > 100 else 1), 0, []

    @staticmethod
    def self_cpu():
        fields = read("/proc/self/stat").rsplit(")", 1)[-1].split()
        return (int(fields[11]) + int(fields[12])) / os.sysconf("SC_CLK_TCK")

    def sample(self):
        cpu, net, at = cpu_times(), net_totals(), time.monotonic()
        loads, rates = core_loads(self.cpu, cpu), net_rates(self.net, net, max(0.5, at - self.at))
        self.cpu, self.net, self.at, self.tick = cpu, net, at, self.tick + 1
        if self.tick % self.gpu_every == 1 or not self.gpus:
            self.gpus = gpus(self.marks)
        memory, swap = meminfo()
        live = {"at": now_iso(), "cores": loads, "memory_used_b": memory["used_b"], "memory_total_b": memory["total_b"],
                "swap_used_b": swap["used_b"], "swap_total_b": swap["total_b"],
                "nets": [{"name": r["name"], "in_bps": r["in_bps"], "out_bps": r["out_bps"]} for r in rates][:32],
                "gpus": [{"index": g["index"], "load": g["load"]} for g in self.gpus]}
        return live, loads, rates, memory, swap

    def snapshot(self, loads, rates, memory, swap):
        uptime = float(read("/proc/uptime").split()[0])
        mhz, (then, cpu_then) = core_mhz(), self.own
        now, cpu_now = time.monotonic(), self.self_cpu()
        self.own = (now, cpu_now)
        share = round(min(100.0, 100.0 * (cpu_now - cpu_then) / max(1.0, now - then)), 2)
        hot = {t["sensor"]: t["now"] for t in temps([])}
        cores = [{"index": i, "load": load, "mhz": mhz[i] if i < len(mhz) else 0.0,
                  "temp": hot.get(f"coretemp Core {i}")} for i, load in enumerate(loads)]
        return {"host": socket.gethostname(), "kernel": os.uname().release, "distro": os_name(),
                "booted_at": datetime.datetime.fromtimestamp(time.time() - uptime, datetime.timezone.utc).isoformat(),
                "uptime_s": int(uptime), "loadavg": [float(v) for v in read("/proc/loadavg").split()[:3]], "cores": cores[:64],
                "memory": memory, "swap": swap, "nets": rates[:32], "disks": disks()[:32], "gpus": self.gpus[:16],
                "temps": temps(self.gpus), "services": services(), "agent_version": VERSION, "agent_cpu_percent": share, "at": now_iso()}

class Poster:
    """One kept-open HTTPS connection to the site, reopened on any error."""

    def __init__(self, site, key):
        self.url, self.key, self.connection = urllib.parse.urlsplit(site), key, None

    def post(self, path, body):
        try:
            if self.connection is None:
                self.connection = http.client.HTTPSConnection(self.url.hostname, self.url.port or 443, timeout=5)
            self.connection.request("POST", path, json.dumps(body), {"Content-Type": "application/json", "X-Admin-Key": self.key})
            response = self.connection.getresponse()
            response.read()
            return response.status
        except (OSError, http.client.HTTPException):
            self.connection = None
            return 0

def load_key():
    path = os.path.join(CONFIG, "admin-key")
    mode = os.stat(path).st_mode & 0o777
    if mode & 0o077:
        sys.exit(f"the key file {path} is readable by others (mode {mode:o}); chmod 600 it")
    key = read(path).strip()
    if len(key) < 16:
        sys.exit(f"the key file {path} holds no key")
    return key

def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else ""
    reader = Reader()
    time.sleep(1)
    live, loads, rates, memory, swap = reader.sample()
    if mode == "--dry-run":
        print(json.dumps({"nvidia_smi_ms": reader.gpu_ms, "gpu_every_ticks": reader.gpu_every,
                          "snapshot": reader.snapshot(loads, rates, memory, swap), "live": live}, indent=2))
        return
    poster = Poster(read(os.path.join(CONFIG, "site")).strip(), load_key())
    if mode == "--once":
        print("snapshot", poster.post("/api/admin/cloud/box/snapshot", reader.snapshot(loads, rates, memory, swap)))
        print("live", poster.post("/api/admin/cloud/box/live", live))
        return
    stopping = []
    signal.signal(signal.SIGTERM, lambda *_: stopping.append(True))
    waiting, failures, posts, state, last_summary, backoff = [], 0, 0, "", time.monotonic(), 0
    while not stopping:
        started = time.monotonic()
        live, loads, rates, memory, swap = reader.sample()
        if reader.tick % SNAPSHOT_EVERY == 1:
            waiting = (waiting + [reader.snapshot(loads, rates, memory, swap)])[-KEEP_SNAPSHOTS:]
        ok = poster.post("/api/admin/cloud/box/live", live) == 200
        while ok and waiting:
            ok = poster.post("/api/admin/cloud/box/snapshot", waiting[0]) in (200, 400)
            if ok:
                waiting.pop(0)
        posts, failures = posts + ok, failures + (not ok)
        now_state = "posting" if ok else "backing off"
        if now_state != state:
            print(now_state if state or not ok else "posting", flush=True)
            state = now_state
        if time.monotonic() - last_summary >= 600:
            print(f"posts {posts}, failures {failures}, agent cpu {reader.self_cpu():.1f} s", flush=True)
            last_summary, posts, failures = time.monotonic(), 0, 0
        backoff = 0 if ok else min(backoff + 1, len(BACKOFF))
        wait = TICK if ok else BACKOFF[backoff - 1]
        time.sleep(max(0.0, wait - (time.monotonic() - started)))

if __name__ == "__main__":
    main()
