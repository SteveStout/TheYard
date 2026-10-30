"""Quality scan for TheYard: architecture, documentation, async hygiene and tests.

Does: reads a checkout (no build) and prints one JSON object of measurements, so two
scans taken a week apart can be diffed number by number, and holds the "ratchet" block
of those measurements to a file of limits that may only move the good way.
Does not: run the suites or measure coverage; .github/coverage.py owns that number, and
its 90 per cent line and 75 per cent branch floors are not this file's to touch.
Usage: python3 .github/quality.py . > scan.json
       python3 .github/quality.py . --ratchet .github/quality-ratchet.json
With --ratchet it prints each number beside its ceiling (or floor) and exits 1 when any
number moved the wrong way, so CI and the ship gate hold the line without anyone having
to remember it. A fix lowers the limit in the same commit, which is the whole mechanism:
the file is the high-water mark, and water only goes out.

Why a script and not an analyzer: every number here is a count a reader can reproduce by
eye on one file, the regular expressions are in front of them, and the same command runs
on a laptop and on the runner. An analyzer would be more exact and nobody would read it.

The record is ADR: The numbers that may only fall, which says what each number measures
and why it may only fall.
"""
import json
import os
import re
import sys
from collections import defaultdict
from pathlib import Path

# The checkout to read: the first argument when one is given, and otherwise the folder
# this script sits in, one level under the repository root, so a bare run works from
# anywhere. A leading dash is a flag, never a path.
ROOT = Path(sys.argv[1]) if len(sys.argv) > 1 and not sys.argv[1].startswith("-") else Path(__file__).resolve().parent.parent

SKIP = ("node_modules", "/bin/", "/obj/", "/Migrations", "Migrations.Sqlite", "/dist/", ".g.cs")


def files(pattern, under):
    out = []
    for base in under:
        p = ROOT / base
        if p.exists():
            out += [f for f in p.rglob(pattern) if not any(s in f.as_posix() for s in SKIP)]
    return sorted(set(out))


def rel(f):
    return f.relative_to(ROOT).as_posix()


def is_test(f):
    s = rel(f)
    return "Tests" in s or ".Tests" in s or "/tests/" in s or s.startswith("tests/")


CS_ALL = files("*.cs", ["api", "functions", "samples/theshed"])
CS_PROD = [f for f in CS_ALL if not is_test(f) and "Experiment" not in rel(f)]
CS_TEST = [f for f in CS_ALL if is_test(f)]
TS_PROD = [f for f in files("*.ts", ["src"]) + files("*.tsx", ["src"]) if ".test." not in f.name]
TS_TEST = [f for f in files("*.test.ts", ["src", "tests"]) + files("*.test.tsx", ["src", "tests"])]
E2E = files("*.spec.ts", ["tests"])

R = {}


def area(f):
    s = rel(f)
    if s.startswith("functions/"):
        return "functions"
    if s.startswith("samples/"):
        return "theshed"
    parts = s.split("/")
    return parts[1] if len(parts) > 1 else s


# ---------- size and comments ----------
def comment_split(text):
    code = comment = 0
    in_block = False
    for line in text.splitlines():
        t = line.strip()
        if not t:
            continue
        if in_block:
            comment += 1
            if "*/" in t:
                in_block = False
            continue
        if t.startswith("//") or t.startswith("*"):
            comment += 1
        elif t.startswith("/*"):
            comment += 1
            in_block = "*/" not in t
        else:
            code += 1
    return code, comment


size = defaultdict(lambda: {"files": 0, "code": 0, "comment": 0, "over_250": 0, "over_300": 0, "no_comment_40plus": []})
for f in CS_PROD + TS_PROD:
    text = f.read_text(encoding="utf-8", errors="replace")
    code, com = comment_split(text)
    a = area(f) if f.suffix == ".cs" else "react"
    d = size[a]
    d["files"] += 1
    d["code"] += code
    d["comment"] += com
    lines = len(text.splitlines())
    if lines > 250:
        d["over_250"] += 1
    if lines > 300:
        d["over_300"] += 1
    if code >= 40 and com == 0:
        d["no_comment_40plus"].append(rel(f))
for a, d in size.items():
    d["comment_ratio"] = round(d["comment"] / max(d["code"], 1), 2)
R["size"] = dict(size)

# ---------- XML doc coverage on C# declarations ----------
DECL = re.compile(
    r"^\s*(public|internal|protected)\s+(?:(?:static|sealed|abstract|partial|readonly|async|override|virtual|required|new|record|unsafe)\s+)*"
    r"(class|record|struct|interface|enum|[\w<>\[\],?. ]+?\s+\w+\s*(\(|\{|=>|;))"
)
docs = defaultdict(lambda: {"decls": 0, "documented": 0, "undocumented": []})
for f in CS_PROD:
    lines = f.read_text(encoding="utf-8", errors="replace").splitlines()
    for i, line in enumerate(lines):
        if not DECL.match(line) or line.strip().startswith(("public const", "internal const")):
            continue
        j = i - 1
        while j >= 0 and (lines[j].strip().startswith("[") or lines[j].strip().startswith("// #region") or not lines[j].strip()):
            j -= 1
        ok = j >= 0 and lines[j].strip().startswith("///")
        d = docs[area(f)]
        d["decls"] += 1
        d["documented"] += ok
        if not ok and len(d["undocumented"]) < 400:
            d["undocumented"].append(f"{rel(f)}:{i + 1}")
for d in docs.values():
    d["pct"] = round(100 * d["documented"] / max(d["decls"], 1), 1)
R["xml_docs"] = {k: {"decls": v["decls"], "pct": v["pct"], "undocumented_count": v["decls"] - v["documented"], "sample": v["undocumented"][:25]} for k, v in docs.items()}

# ---------- React file headers (Does / Does not / Used by) ----------
hdr_missing = [rel(f) for f in TS_PROD if not re.search(r"Does:|Does not:|Used by", f.read_text(encoding="utf-8", errors="replace")[:1500])]
R["react_headers"] = {"files": len(TS_PROD), "missing": len(hdr_missing), "missing_sample": hdr_missing[:25]}

# ---------- comment hygiene: references standing in for explanations ----------
hyg = defaultdict(int)
hyg_samples = defaultdict(list)
for f in CS_PROD + TS_PROD:
    for n, line in enumerate(f.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
        t = line.strip()
        if not (t.startswith("//") or t.startswith("*") or t.startswith("/*")):
            continue
        for key, rx in (("adr_ref", r"ADR-\d+"), ("version_ref", r"\b1\.0\.\d+\.\d+\b"), ("date_ref", r"\b20\d\d-\d\d-\d\d\b|\b\d{1,2}/\d{1,2}/20\d\d\b"),
                        ("todo", r"\b(TODO|FIXME|HACK|XXX)\b"), ("person", r"\bSteve\b"), ("em_dash", "—")):
            if re.search(rx, t):
                hyg[key] += 1
                if len(hyg_samples[key]) < 8:
                    hyg_samples[key].append(f"{rel(f)}:{n}")
        # a comment whose whole content is a pointer, e.g. "// ADR-115" or "// see ADR-88"
        if re.fullmatch(r"(//+|\*)\s*(see\s+)?\(?ADR-\d+[^a-z]*\)?\.?", t, re.I):
            hyg["pointer_only"] += 1
            if len(hyg_samples["pointer_only"]) < 8:
                hyg_samples["pointer_only"].append(f"{rel(f)}:{n}")
R["comment_hygiene"] = {"counts": dict(hyg), "samples": dict(hyg_samples)}

# ---------- onion: project references ----------
refs = {}
for proj in files("*.csproj", ["api", "functions"]):
    if is_test(proj):
        continue
    t = proj.read_text(encoding="utf-8", errors="replace")
    refs[proj.stem] = sorted(Path(m.replace("\\", "/")).stem for m in re.findall(r'ProjectReference Include="([^"]+)"', t))
R["project_refs"] = refs
rank = {"Data": 0, "Domain": 1, "Application": 2, "Infrastructure": 3, "Cosmos": 3, "Api": 4, "Functions": 4}


def ring(name):
    for k in ("Cosmos", "Infrastructure", "Application", "Domain", "Data", "Api", "Functions"):
        if k in name:
            return rank[k]
    return None


outward = [f"{p} -> {r}" for p, rs in refs.items() for r in rs if ring(p) is not None and ring(r) is not None and ring(r) > ring(p)]
R["onion_outward_refs"] = outward

leak = []
for f in CS_PROD:
    s = rel(f)
    if not re.search(r"\.(Domain|Application|Data)/", s):
        continue
    for u in re.findall(r"^using\s+([\w.]+);", f.read_text(encoding="utf-8", errors="replace"), re.M):
        if re.search(r"Infrastructure|EntityFrameworkCore|Microsoft\.Azure|Azure\.|System\.IO$|System\.Net\.Http|AspNetCore", u):
            leak.append(f"{s}: using {u}")
R["inner_ring_leaks"] = leak

# ---------- CQRS and slices ----------
handlers = defaultdict(lambda: {"query": 0, "command": 0})
for f in CS_PROD:
    t = f.read_text(encoding="utf-8", errors="replace")
    a = "functions" if rel(f).startswith("functions/") else "api"
    handlers[a]["query"] += len(re.findall(r"IQueryHandler<", t)) - (1 if "interface IQueryHandler" in t else 0)
    handlers[a]["command"] += len(re.findall(r"ICommandHandler<", t)) - (1 if "interface ICommandHandler" in t else 0)
R["cqrs_handlers"] = dict(handlers)

direct = []
PORT = re.compile(r"\b(I\w+(Store|Writer|Repository|Catalogue|Source)|DbContext|CosmosClient|Container\b)")
for f in CS_PROD:
    s = rel(f)
    if re.search(r"TheYard\.Api/Features/.+(Endpoints|Module)\.cs$", s) or re.search(r"functions/jobhunt/.+Function\w*\.cs$", s):
        t = f.read_text(encoding="utf-8", errors="replace")
        for m in sorted(set(x[0] for x in PORT.findall(t))):
            direct.append(f"{s}: {m}")
R["endpoints_touching_ports"] = direct

cross = []
for f in CS_PROD:
    s = rel(f)
    m = re.search(r"/Features/(\w+)/", s)
    if not m:
        continue
    own = m.group(1)
    for u in re.findall(r"^using\s+([\w.]+);", f.read_text(encoding="utf-8", errors="replace"), re.M):
        mm = re.search(r"\.Features\.(\w+)", u)
        if mm and mm.group(1) != own:
            cross.append(f"{s} -> Features.{mm.group(1)}")
R["cross_slice_usings"] = cross
R["slices"] = sorted(p.name for p in (ROOT / "api/TheYard.Api/Features").iterdir() if p.is_dir()) if (ROOT / "api/TheYard.Api/Features").exists() else []
R["slices_without_module"] = [s for s in R["slices"] if not (ROOT / f"api/TheYard.Api/Features/{s}/Module.cs").exists()]

# ---------- SOLID proxies and async hygiene ----------
# #region whole-signature
# The parameter list of a declaration that starts on a line, read by balancing brackets
# from the method's own opening one and running on to the lines below it until they close.
# A parameter list is not "everything up to the first closing bracket": an attribute brings
# its own pair ([TimerTrigger("0 5 * * * *")]), so does a tuple parameter ((string Path,
# bool Descending) key), and a signature wide enough to wrap puts its last parameters on
# the lines after the one the declaration starts on. Reading to the first bracket counted
# eight methods that declare a CancellationToken as methods that do not, which is a defect
# in the count and not in the code (ADR: The numbers that may only fall, the addendum on
# reading a whole signature). RUN_ON is the look-ahead: no signature in this codebase wraps
# further, and a declaration whose brackets never close falls back to the line itself, so a
# file the reader cannot parse is still counted rather than silently skipped.
RUN_ON = 12


def parameters(lines, start, open_at):
    """The text between a declaration's brackets, across as many lines as it takes."""
    depth = 0
    out = []
    for i in range(start, min(start + RUN_ON, len(lines))):
        text = lines[i].split("//")[0] if i > start else lines[i].split("//")[0][open_at:]
        for ch in text:
            if ch == "(":
                depth += 1
                if depth == 1:
                    continue
            elif ch == ")":
                depth -= 1
                if depth == 0:
                    return "".join(out)
            if depth >= 1:
                out.append(ch)
        out.append(" ")
    return None
# #endregion whole-signature

smell = defaultdict(list)
for f in CS_PROD:
    s = rel(f)
    t = f.read_text(encoding="utf-8", errors="replace")
    all_lines = t.splitlines()
    for n, line in enumerate(all_lines, 1):
        code = line.split("//")[0]
        if re.search(r"\.Result\b|\.Wait\(\)|GetAwaiter\(\)\.GetResult\(\)", code):
            smell["blocking_wait"].append(f"{s}:{n}")
        if re.search(r"\basync\s+void\b", code):
            smell["async_void"].append(f"{s}:{n}")
        if re.search(r"DateTime(Offset)?\.(Now|UtcNow)\b", code):
            smell["clock_direct"].append(f"{s}:{n}")
        if "Console.Write" in code:
            smell["console_write"].append(f"{s}:{n}")
        if re.search(r"catch\s*(\([^)]*\))?\s*\{\s*\}", code):
            smell["empty_catch"].append(f"{s}:{n}")
        if "#pragma warning disable" in code or "SuppressMessage" in code:
            smell["suppression"].append(f"{s}:{n}")
        m = re.search(r"\b(public|internal|private|protected)\s+(?:static\s+|override\s+|virtual\s+)*async\s+[\w<>\[\],? ]+\s+(\w+)\s*\(", code)
        if m and not re.search(r"\bMain\b|Dispose", m.group(2)) and line.rstrip().endswith((")", "{", ",")):
            taken = parameters(all_lines, n - 1, m.end() - 1)
            if "CancellationToken" not in (taken if taken is not None else code):
                smell["async_no_token"].append(f"{s}:{n} {m.group(2)}")
        m = re.search(r"\b(class|record)\s+\w+(<[^>]*>)?\s*\(([^)]*)\)", code)
        if m and m.group(3).count(",") >= 5:
            smell["ctor_6plus_params"].append(f"{s}:{n}")
        m = re.search(r"^\s*public\s+\w+\s*\(([^)]*)\)\s*$", code)
        if m and m.group(1).count(",") >= 5:
            smell["ctor_6plus_params"].append(f"{s}:{n}")
        if re.search(r"^\s*(public|internal)\s+(partial\s+)?class\s", code) and "abstract" not in code and "static" not in code:
            smell["unsealed_class"].append(f"{s}:{n}")
        if re.search(r"\bstatic\s+(?!readonly|class|async|void|bool|int|string|Task|IEnumerable|IReadOnly)\w[\w<>,? ]*\s+\w+\s*(=|;)", code) and "const" not in code and "=>" not in code and "(" not in code.split("=")[0]:
            smell["static_mutable"].append(f"{s}:{n}")
R["smells"] = {k: {"count": len(v), "sample": v[:12]} for k, v in smell.items()}

# #region constructor-width
# Service classes (not records) handed six or more dependencies: the SRP signal that matters.
# The count is of PARAMETERS, read the way a reader reads them, and it used to be of commas on
# one line. Two different things: a generic type argument carries a comma of its own, so
# Func<CancellationToken, Task<string?>> was read as two parameters and three classes taking
# four and five were counted as taking six; and a parameter list wide enough to wrap puts its
# last parameters on the lines below, so six classes taking six to eight were not counted at
# all. The whole-signature helper above already reads a declaration's brackets across the lines
# it takes, so this rule now reads the list through it and splits that list only where a reader
# would: a comma at the list's own bracket depth, outside any angle brackets (ADR: The numbers
# that may only fall, the addendum on counting a constructor's width).
#
# What this still does not see, said out loud rather than left for the next reader to find: a
# traditional constructor, "public Thing(...)" inside the class body. Telling a class's
# constructor from a record's would mean tracking the enclosing type, which is more than a line
# reader can do, and the one production type it hides is named in the record's addendum.
def handed(taken):
    """The parameters in a parameter list, split where a reader would split them."""
    if taken is None:
        return []
    depth = angle = 0
    out, cur = [], ""
    for ch in taken:
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
        elif ch == "<":
            angle += 1
        elif ch == ">":
            angle = max(0, angle - 1)
        if ch == "," and depth == 0 and angle == 0:
            out.append(cur)
            cur = ""
        else:
            cur += ch
    out.append(cur)
    return [p.strip() for p in out if p.strip()]


PRIMARY = re.compile(r"^\s*(public|internal)\s+(sealed\s+|partial\s+|abstract\s+)*class\s+\w+(<[^>]*>)?\s*\(")
WIDE_AT = 6

wide = []
for f in CS_PROD:
    all_lines = f.read_text(encoding="utf-8", errors="replace").splitlines()
    for n, line in enumerate(all_lines, 1):
        m = PRIMARY.search(line.split("//")[0])
        if m and len(handed(parameters(all_lines, n - 1, m.end() - 1))) >= WIDE_AT:
            wide.append(f"{rel(f)}:{n}")
R["wide_service_classes"] = {"count": len(wide), "list": wide}
# #endregion constructor-width

# Clock read directly outside a composition root, where TimeProvider or the Clocks seam should be handed in.
clock = [x for x in smell["clock_direct"] if not re.search(r"Module\.cs|/Wiring/|Program\.cs|Clocks\.cs", x)]
R["clock_outside_composition"] = {"count": len(clock), "sample": clock[:15]}

# #region history-rule
# Comments leaning on provenance in place of the what, how and why: when the code was written, who
# asked for it, which version shipped it, which review found it. The shapes are the ones
# tests/rules/HistoryRule.cs asks, kept the same deliberately, because a count and a rule that
# disagreed would be describing two different codebases.
#
# Two things make this harder than a list of forbidden words. A date can be the subject rather than
# the history: a comment naming the wire format a function parses has to show it, so a date inside
# backticks or quotes is an example of a format and not a note about the past, and those spans come
# out before the line is read. And the three lines whose quotation runs across two source lines are
# named one by one, because a stripper cannot pair a quote with its other half on the next line.
#
# A day beside a month name counts only when no four-digit year follows it. A recent date in this
# project's own past is written the way a person writes one, "23 Oct", because the year is this one,
# while a date that spells its year out is naming a fixed point a comment may legitimately need, and
# the one in this repository is the Unix epoch. A bare slash date is never read alone, because
# "90/75" is the coverage floor and "1/2" is a half; it counts beside the owner's name or beside a
# word for an instruction. His bare name and a bare month name are provenance in the site's own code
# and the subject in the job hunt's, which is why HistoryRule asks in two strengths; this number asks
# only the shapes that are provenance in both, so it never counts a clock's November or an inbox's
# owner.
PROVENANCE = re.compile(
    r"\b20\d\d-\d\d(-\d\d)?\b"
    r"|\b\d{1,2}/\d{1,2}/20\d\d\b"
    r"|\b\d{1,2} (Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)[a-z]*\b(?! \d{4})"
    r"|\b(Steve|he)('s)? (said|asked|named|wants|ruled|word|call)\b"
    r"|\b(Steve|he|his)\b[^.]{0,40}?\b\d{1,2}/\d{1,2}\b"
    r"|\b\d{1,2}/\d{1,2}\b[^.]{0,40}?\b(rule|ask|asked|said|named|wants|ruled|call)\b"
    r"|\b1\.0\.\d+\.\d+\b"
    r"|the review of|the staff review|the tweaks pass|the self-review")
# The lines whose quotation runs across two source lines: the chart's axis labels, and the opening
# line of a decision record as the document layout matches it. A file name and a fragment that line
# holds, which is narrower than a file name alone and survives the line moving.
FORMAT_EXAMPLES = (
    ("docLayout.ts", "shipped as 1.0.0.21."),
    ("dayMarks.ts", '"Mon 5 Oct, Fri 9,'),
    ("dayMarks.ts", 'Mon 12, Fri 16"'),
    ("RunLine.cs", "process:Merger outcome=success"),
)


def comment_of(line):
    """The part of a line that is a comment: the whole line when it opens with one, else a trailing one."""
    text = line.lstrip()
    if text.startswith(("//", "*", "/*", "{/*")):
        return line
    at = line.find(" // ")
    return line[at:] if at >= 0 else ""


def leans_on_history(path, line):
    """Whether this line's comment leans on provenance, its format examples taken out first."""
    if any(path.endswith("/" + name) and fragment in line for name, fragment in FORMAT_EXAMPLES):
        return False
    return bool(PROVENANCE.search(re.sub(r"`[^`]*`|\"[^\"]*\"", "", comment_of(line))))


hist = []
for f in CS_PROD + TS_PROD:
    for n, line in enumerate(f.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
        if not line.strip().startswith("/// <param") and leans_on_history(rel(f), line):
            hist.append(f"{rel(f)}:{n}")
R["history_or_name_comments"] = {"count": len(hist), "sample": hist[:15]}
# #endregion history-rule

# Files within ten lines of the 300-line cap: the next edit forces a split, so plan it.
near = []
for f in CS_PROD:
    n = len(f.read_text(encoding="utf-8", errors="replace").splitlines())
    if n >= 290:
        near.append(f"{rel(f)} ({n})")
R["near_line_cap"] = near

# ---------- tests ----------
facts = sum(len(re.findall(r"\[(Fact|Theory)\b", f.read_text(encoding="utf-8", errors="replace"))) for f in CS_TEST)
rows = sum(len(re.findall(r"\[InlineData\b", f.read_text(encoding="utf-8", errors="replace"))) for f in CS_TEST)
vit = sum(len(re.findall(r"^\s*(it|test)\(", f.read_text(encoding="utf-8", errors="replace"), re.M)) for f in TS_TEST)
pw = sum(len(re.findall(r"^\s*test\(", f.read_text(encoding="utf-8", errors="replace"), re.M)) for f in E2E)
test_loc = sum(comment_split(f.read_text(encoding="utf-8", errors="replace"))[0] for f in CS_TEST + TS_TEST + E2E)
prod_loc = sum(comment_split(f.read_text(encoding="utf-8", errors="replace"))[0] for f in CS_PROD + TS_PROD)
test_text = "\n".join(f.read_text(encoding="utf-8", errors="replace") for f in CS_TEST + TS_TEST + E2E)
untested = []
for f in CS_PROD:
    for name in re.findall(r"^\s*(?:public|internal)\s+(?:sealed\s+|static\s+|partial\s+)*(?:class|record)\s+(\w+(?:Handler|Store|Rules|Reader|Writer|Service|Policy|Calculator))\b", f.read_text(encoding="utf-8", errors="replace"), re.M):
        if not re.search(r"\b" + name + r"\b", test_text):
            untested.append(f"{name} ({rel(f)})")
R["tests"] = {"dotnet_facts_and_theories": facts, "inline_data_rows": rows, "vitest": vit, "playwright": pw,
              "test_loc": test_loc, "prod_loc": prod_loc, "test_to_prod": round(test_loc / max(prod_loc, 1), 2),
              "behavior_types_never_named_in_a_test": len(untested), "untested_sample": untested[:30]}

# ---------- best-practice documents ----------
bp = ROOT / "docs/best-practices"
R["best_practice_docs"] = sorted(p.name for p in bp.glob("*.md")) if bp.exists() else []
pattern_doc = {}
for pat, rx in (("cqrs", r"CQRS|query or a command"), ("onion", r"onion"), ("solid", r"SOLID"), ("vertical_slices", r"vertical slice"), ("modular_monolith", r"modular monolith")):
    hits = [rel(p) for p in (ROOT / "docs").rglob("*.md") if re.search(rx, p.read_text(encoding="utf-8", errors="replace"), re.I)]
    pattern_doc[pat] = len(hits)
R["docs_mentioning_pattern"] = pattern_doc
R["adr_count"] = len(list((ROOT / "docs/decisions").glob("ADR-*.md"))) if (ROOT / "docs/decisions").exists() else 0

# #region ratchet
# The ratchet: one number per line, lower is better unless the name says otherwise.
R["ratchet"] = {
    "undocumented_declarations": sum(v["decls"] - v["documented"] for v in docs.values()),
    "onion_outward_refs": len(outward),
    "inner_ring_leaks": len(leak),
    "cross_slice_usings": len(cross),
    "wide_service_classes": len(wide),
    "async_without_cancellation": len(smell["async_no_token"]),
    "clock_outside_composition": len(clock),
    "history_or_name_comments": len(hist),
    "blocking_waits": len(smell["blocking_wait"]),
    "files_over_300": sum(d["over_300"] for d in size.values()),
    "files_within_10_of_cap": len(near),
    "behavior_types_never_named_in_a_test": len(untested),
    "test_to_prod_ratio_higher_is_better": R["tests"]["test_to_prod"],
    "dotnet_tests_higher_is_better": facts + rows,
}
# #endregion ratchet

# The ratchet, read against its file. Output is shaped the way .github/coverage.py shapes
# its own: a line per number for anyone reading the log, GitHub annotations for a reader
# who is not signed in, a job summary for one who is, and an exit code for the build. A
# red step whose only public word is "exit code 1" costs somebody a session.
if "--ratchet" in sys.argv:
    limits = json.load(open(sys.argv[sys.argv.index("--ratchet") + 1], encoding="utf-8"))
    worse, slack, lines = [], [], []
    for key, limit in limits.items():
        now = R["ratchet"].get(key)
        floor = key.endswith("higher_is_better")
        bad = now is None or (now < limit if floor else now > limit)
        print(f"{'WORSE' if bad else 'ok   '} {key}: {now} (limit {limit})")
        lines.append(f"| {key} | {now} | {limit} | {'WORSE' if bad else 'ok'} |")
        if bad:
            worse.append(f"{key} is {now} against a limit of {limit}")
        elif now != limit:
            slack.append(f"{key} is {now} and the file still says {limit}")

    summary = ["## Quality ratchet", "", "| Number | Now | Limit | |", "| --- | ---: | ---: | --- |", *lines]
    where = os.environ.get("GITHUB_STEP_SUMMARY")
    if where:
        with open(where, "a", encoding="utf-8") as handle:
            handle.write(chr(10).join(summary) + chr(10))

    for line in worse:
        print(f"::error::{line}; fix it, or say here why the limit moves the wrong way")
    # A number better than its limit is not a failure: it is a limit waiting to be lowered
    # in the commit that earned it. Said out loud, because a ratchet nobody tightens is a
    # ratchet that only ever holds the worst week this project has had.
    for line in slack:
        print(f"::notice::{line}; lower the file in the commit that earned it")
    if not worse:
        print(f"::notice::quality ratchet: {len(limits)} numbers, none worse")
    sys.exit(1 if worse else 0)

print(json.dumps(R, indent=1))
