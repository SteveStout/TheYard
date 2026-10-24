#!/usr/bin/env python3
"""
ai_tells_five_signs.py - the second AI-tells gate, built 2026-09-13.

Companion to ai_tells_scan.py, not a replacement. That script checks how the
prose SOUNDS (elevated openers, abstract nouns, cadence, absolutes). This one
checks the five things a hiring manager actually looks for, from
"5 Signs That Indicate To Hiring Managers You Used AI on Your Resume",
A Life After Layoff, YouTube MxBZjnayQUM, sent by Steve 2026-09-13.

LABELLED: the video's audio was not readable from the session that wrote this.
The five signs came from a transcript-service summary, paraphrased. The word
lists and thresholds are this system's, not the video's.

  S1  telltale vocabulary            FAIL on any hit
  S2  metrics with no context        WARN, needs judgement
  S3  mirroring the job description  FAIL over the threshold, needs --jd
  S4  hidden text in a .docx         FAIL on any hit
  S5  tech he has never shipped      WARN, and every hit must be labelled

Stdlib only. No network. Reads .txt, .md and .docx.

Usage:
  python ai_tells_five_signs.py --resume Steven_Stout_Resume_2026-09-13.docx
  python ai_tells_five_signs.py --text DRAFTS-MONDAY-2026-09-14.md --text writing-samples.md
  python ai_tells_five_signs.py --resume r.docx --jd posting.txt

Exit code 1 if any check FAILs, so it can sit in a ship gate.
"""
import argparse
import os
import re
import sys
import zipfile
from html import unescape

# ------------------------------------------------------------------ S1 words
# Added 2026-09-13 from the five-signs video, on top of ai_tells_scan.py's list.
S1_WORDS = [
    "leverage", "leveraged", "leverages", "leveraging",
    "pivoted", "pivoting", "spearhead", "spearheaded", "spearheading",
    "orchestrate", "orchestrated", "orchestrating",
    "streamline", "streamlined", "streamlining",
    "synergy", "synergies", "synergistic", "robust", "delve", "delved", "delving",
    "foster", "fostered", "fostering", "facilitate", "facilitated", "facilitating",
    "myriad", "plethora", "tapestry", "realm", "landscape",
    "testament", "embark", "embarked", "vibrant", "crucial", "pivotal",
    "cultivate", "cultivated", "cultivating",
    "holistic", "seamless", "seamlessly", "cutting-edge", "best-in-class",
    "results-driven", "meticulous", "meticulously", "utilize", "utilized", "utilizing",
    "passionate", "innovative", "empower", "empowered", "empowering",
    "elevate", "elevated",
]
# Words that are only a tell when they take an abstract object. "navigated the
# app's router", "underscores in a test name" and "an unlocked origin" are all
# literal and correct; "navigating complexity", "underscores the importance" and
# "unlocking value" are the model's voice. Measured 2026-09-13: the bare-word
# version of these produced five hits across 85 TheYard documents and every one
# was literal. A gate that cries wolf gets ignored, so they moved here.
S1_CONTEXTUAL = [
    r"\bnavigat(?:e|ed|ing|es)\s+(?:the\s+|a\s+|this\s+)?(?:complex|complexit|challeng|ambigu"
    r"|landscape|nuance|tension|tradeoff|uncertain|dynamic)",
    r"\bunderscor(?:e|es|ed|ing)\s+(?:the|how|why|just|our|its)\b",
    r"\bunlock(?:s|ed|ing)?\s+(?:the\s+|new\s+|its\s+|greater\s+)?"
    r"(?:potential|value|growth|insight|opportunit|capabilit|efficienc)",
    r"\bharness(?:es|ed|ing)?\s+(?:the\s+)?(?:power|potential|capabilit)",
]

# Phrases, checked whole.
S1_PHRASES = [
    "leverage synergies", "optimize outcomes", "proven track record",
    "deep dive", "drive impact", "drive value", "drive results",
    "wide range of", "fast-paced environment", "wearing many hats",
]

# --------------------------------------------------------- S2 metric context
# Only claim-shaped numbers count: a percent, a dollar figure, or a magnitude
# written with a plus. A phone number, a date, a version and a file size are not
# claims, so they are excluded before the check runs.
NUMBER = re.compile(r"(\$\s?[\d,]{2,}|\b\d+(\.\d+)?\s?%|\b\d[\d,]*\+(?![\d-]))")
CONTEXT = re.compile(
    r"\b(through|by|so|which|because|after|while|without|per|across|"
    r"in order to|that let|that gave|enabling|resulting|meaning|worth of|out of)\b", re.I)
SKIP_LINE = re.compile(
    r"^\s*(#|\||>|-{3,}|```)"                 # headings, tables, rules, fences
    r"|\b\d{4}-\d{2}-\d{2}\b"                 # ISO dates
    r"|\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]* \d{4}\b"
    r"|\b\d{3}-\d{3}-\d{4}\b"                 # phone
    r"|\bL\d+\b"                               # line references
    r"|\b\d+(\.\d+){2,}\b",                    # version strings
    re.I)

# ---------------------------------------------------------------- S5 defaults
# Technologies with zero years on Steve's record as of 2026-09-13, per
# standard-answers.md and candidate-profile.md. Every hit is reported, and a
# deliberate, labelled "equivalent" framing is still a hit: the point is that he
# knows every place it appears, not that it is forbidden.
S5_DEFAULT = ["aws", "amazon web services", "golang", "rust", "kubernetes",
              "node.js", "nodejs", "terraform", "kafka", "scala", "ruby", "php"]

DOCX_COLOR = re.compile(r'w:color[^/>]*w:val="(?P<v>[0-9A-Fa-f]{6})"')
DOCX_SZ = re.compile(r'w:sz[^/>]*w:val="(?P<v>\d+)"')


def load_text(path):
    ext = os.path.splitext(path)[1].lower()
    if ext == ".docx":
        with zipfile.ZipFile(path) as z:
            xml = z.read("word/document.xml").decode("utf-8", "replace")
        xml = re.sub(r"</w:p>", "\n", xml)
        txt = re.sub(r"<[^>]+>", "", xml)
        return unescape(txt)
    with open(path, encoding="utf-8", errors="replace") as fh:
        return fh.read()


def load_docx_runs(path):
    """Yield (run_xml, run_text) for every run in a .docx."""
    with zipfile.ZipFile(path) as z:
        xml = z.read("word/document.xml").decode("utf-8", "replace")
    for m in re.finditer(r"<w:r>(?:(?!</w:r>).)*</w:r>", xml, re.S):
        run = m.group(0)
        text = unescape(re.sub(r"<[^>]+>", "", re.sub(r"</w:t>", "", run)))
        yield run, text


QUOTED = re.compile(r'"[^"]{1,600}"', re.S)


def strip_quoted(text):
    """Blank out double-quoted spans, newlines preserved.

    A tell word inside quotation marks is somebody else's wording being quoted
    back (a job posting, an intake form, an interviewer), not Steve writing like
    a model. Quoting their language is good practice, so it is not a hit. The
    span may wrap across lines, and line numbers must stay honest, so newlines
    survive the blanking.
    """
    def blank(m):
        return "".join("\n" if c == "\n" else " " for c in m.group(0))
    return QUOTED.sub(blank, text)


def s1_vocabulary(text):
    """Telltale vocabulary in his own prose and spoken answers.

    Skips markdown tables, blockquotes and headings: those hold other people's
    wording (a posting, a screening question) recorded verbatim, and a hit there
    says nothing about how Steve writes.
    """
    hits = []
    for i, line in enumerate(strip_quoted(text).split("\n"), 1):
        if SKIP_LINE.search(line):
            continue
        low = line.lower()
        for w in S1_WORDS:
            for _ in re.finditer(r"\b" + re.escape(w) + r"\b", low):
                hits.append((w, i))
        for ph in S1_PHRASES:
            for _ in re.finditer(re.escape(ph), low):
                hits.append((ph, i))
        for rx in S1_CONTEXTUAL:
            for m in re.finditer(rx, low):
                hits.append((m.group(0)[:28], i))
    return hits


def line_of(text, idx):
    return text.count("\n", 0, idx) + 1


def s2_metric_context(text):
    out = []
    for i, line in enumerate(text.split("\n"), 1):
        s = line.strip()
        if not s or len(s) < 25 or SKIP_LINE.search(s):
            continue
        if NUMBER.search(s) and not CONTEXT.search(s):
            out.append((i, s[:110]))
    return out


def s3_jd_mirror(text, jd_text, min_words=6):
    """Longest shared word runs between the document and a job posting."""
    def norm(t):
        return re.findall(r"[a-z0-9+#.]+", t.lower())
    a, b = norm(text), norm(jd_text)
    bset = set()
    for n in range(min_words, min_words + 6):
        for i in range(len(b) - n + 1):
            bset.add(tuple(b[i:i + n]))
    hits = []
    for n in range(min_words, min_words + 6):
        for i in range(len(a) - n + 1):
            chunk = tuple(a[i:i + n])
            if chunk in bset:
                hits.append(" ".join(chunk))
    # keep the longest, drop substrings
    hits.sort(key=len, reverse=True)
    kept = []
    for h in hits:
        if not any(h in k for k in kept):
            kept.append(h)
    return kept[:10]


def s4_hidden_text(path):
    out = []
    for run, text in load_docx_runs(path):
        stripped = text.strip()
        col = DOCX_COLOR.search(run)
        sz = DOCX_SZ.search(run)
        vanish = "<w:vanish" in run
        pts = int(sz.group("v")) / 2.0 if sz else None
        if vanish and stripped:
            out.append(("w:vanish", stripped[:60]))
        elif col and col.group("v").upper() in ("FFFFFF", "FEFEFE") and stripped:
            out.append(("white text", stripped[:60]))
        elif pts is not None and pts < 6 and stripped:
            out.append(("%.1fpt text" % pts, stripped[:60]))
    return out


def s5_misalignment(text, terms):
    low = text.lower()
    out = []
    for t in terms:
        for m in re.finditer(r"\b" + re.escape(t) + r"\b", low):
            out.append((t, line_of(text, m.start())))
    return out


def report(name, results):
    worst = "PASS"
    print("=" * 78)
    print(name)
    for code, status, detail in results:
        print("  %-4s %-5s %s" % (code, status, detail))
        if status == "FAIL":
            worst = "FAIL"
        elif status == "WARN" and worst == "PASS":
            worst = "WARN"
    print("  RESULT: %s" % worst)
    return worst


def run_one(path, jd_text, s5_terms):
    text = load_text(path)
    res = []

    h = s1_vocabulary(text)
    if h:
        shown = ", ".join(sorted({"%s (L%d)" % (w, l) for w, l in h})[:8])
        res.append(("S1", "FAIL", "%d vocabulary hits: %s" % (len(h), shown)))
    else:
        res.append(("S1", "PASS", "no telltale vocabulary"))

    m = s2_metric_context(text)
    if m:
        res.append(("S2", "WARN", "%d numbers with no causal clause on the line, first: L%d %s"
                    % (len(m), m[0][0], m[0][1])))
    else:
        res.append(("S2", "PASS", "every number carries context on its own line"))

    if jd_text:
        k = s3_jd_mirror(text, jd_text)
        if k:
            res.append(("S3", "FAIL", "%d shared runs of 6+ words with the posting, longest: \"%s\""
                        % (len(k), k[0])))
        else:
            res.append(("S3", "PASS", "no 6-word run shared with the posting"))
    else:
        res.append(("S3", "SKIP", "no --jd given"))

    if path.lower().endswith(".docx"):
        hid = s4_hidden_text(path)
        if hid:
            res.append(("S4", "FAIL", "; ".join("%s: %r" % x for x in hid[:4])))
        else:
            res.append(("S4", "PASS", "no white, hidden or sub-6pt text carrying content"))
    else:
        res.append(("S4", "SKIP", "not a .docx"))

    mis = s5_misalignment(text, s5_terms)
    if mis:
        shown = ", ".join(sorted({"%s (L%d)" % (t, l) for t, l in mis})[:8])
        res.append(("S5", "WARN", "%d mentions of tech with zero years on file: %s" % (len(mis), shown)))
    else:
        res.append(("S5", "PASS", "no tech outside his record"))

    return report(os.path.basename(path), res)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--resume")
    ap.add_argument("--text", action="append", default=[])
    ap.add_argument("--jd")
    ap.add_argument("--zero-years", default=",".join(S5_DEFAULT))
    a = ap.parse_args()

    jd_text = load_text(a.jd) if a.jd else None
    terms = [t.strip().lower() for t in a.zero_years.split(",") if t.strip()]

    paths = ([a.resume] if a.resume else []) + list(a.text)
    if not paths:
        ap.error("give --resume and/or --text")

    worst = "PASS"
    for p in paths:
        r = run_one(p, jd_text, terms)
        if r == "FAIL":
            worst = "FAIL"
        elif r == "WARN" and worst == "PASS":
            worst = "WARN"
    print("=" * 78)
    print("OVERALL: %s" % worst)
    return 1 if worst == "FAIL" else 0


if __name__ == "__main__":
    sys.exit(main())
