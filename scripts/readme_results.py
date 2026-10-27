"""Write the README's and Built with AI's results figures from data/test-results.json, so every number they quote is
the gate's own for the version it names. Run from the repository's root after scripts/test_results.mjs; the gate's
promote job does (ADR: The gate runs where the memory is, the addendum of 1.0.4.101), and so does a person promoting
a run by hand. Prints what it wrote."""
import json, re, sys

d = json.load(open("data/test-results.json", encoding="utf-8"))
ver = d["version"]
s = {x["id"]: x for x in d["suites"]}
def n(i): return s[i]["passed"] + s[i]["failed"]
total = sum(n(i) for i in s)
declared = sum(1 for p in __import__("glob").glob("tests/e2e/*.spec.ts") for l in open(p, encoding="utf-8") if re.match(r"^\s*test\(", l))
def carried(i):
    c = s[i].get("carried") or s[i].get("carriedFrom")
    return f", carried forward from {c}" if c else ""
para = (f"**How {total:,} is counted**, from the gate's own results file for {ver} "
        f"([`data/test-results.json`](https://github.com/SteveStout/TheYard/blob/main/data/test-results.json)): "
        f"{n('vitest')} Vitest tests, {n('xunit-sqlite')} xUnit tests on SQLite and the same {n('xunit-cosmos')} booted on Cosmos DB{carried('xunit-cosmos')}, "
        f"the {n('xunit-live')} that need the live Cosmos DB account, {n('browser-sqlite')} browser runs on SQLite from the {declared} declared tests "
        f"(a loop runs one of them more than once) and {n('browser-cosmos')} of those again on Cosmos DB{carried('browser-cosmos')}, which is {total:,}. "
        f"The xUnit and browser counts above are read from the source; a test written after {ver} first runs in the gate of the version that ships it.")

if "passed only on its retry" in open("api/TheYard.Tests/TestResultsTests.cs", encoding="utf-8").read():
    para += " A browser test that passes only on Playwright's one retry is written 'r', never a pass, and a file holding one is refused, so every run counted here passed the first time."

def rewrite(path, subs):
    t = open(path, encoding="utf-8").read(); before = t
    for pat, rep in subs:
        t, k = re.subn(pat, rep, t)
        if k == 0: sys.exit(f"{path}: no match for {pat}")
    open(path, "w", encoding="utf-8", newline="\n").write(t)
    print(f"{path}: {'changed' if t != before else 'unchanged'}")

rewrite("README.md", [
    (r"\d+ Vitest tests at \d+\.\d+\.\d+\.\d+", f"{n('vitest')} Vitest tests at {ver}"),
    (r"the gate's results for \d+\.\d+\.\d+\.\d+ hold [\d,]+ test runs", f"the gate's results for {ver} hold {total:,} test runs"),
    (r"\| Vitest \| \d+ at \d+\.\d+\.\d+\.\d+ \|", f"| Vitest | {n('vitest')} at {ver} |"),
    (r"(?m)^\*\*How [\d,]+ is counted\*\*.*$", para.replace("\\", "\\\\")),
])
# The review of the architecture, finding 5: once PublicFaceTests holds the README's xUnit number to this
# file (The_test_count_in_the_readme_is_the_gate_s_count), every place the README and Built with AI state it is written here
# from xunit-sqlite, after the gate, in the same commit. An older tree, whose test counts Facts and InlineData, is left alone.
gate_count = "The_test_count_in_the_readme_is_the_gate_s_count" in open("api/TheYard.Tests/PublicFaceTests.cs", encoding="utf-8").read()
if gate_count:
    x = n('xunit-sqlite')
    rewrite("README.md", [
        (r"(hold |gate: )\d+( xUnit tests)", lambda m: f"{m.group(1)}{x}{m.group(2)}"),
        (r"\(\d+( xUnit tests, separate)", lambda m: f"({x}{m.group(1)}"),
        (r"(\| API \| xUnit \| )\d+", lambda m: f"{m.group(1)}{x}"),
    ])
# Built with AI lives in docs/built-with-ai/; the old path at docs/ is read only on a tree that still has it there.
import os
bwa = "docs/built-with-ai/BUILT-WITH-AI.md" if os.path.exists("docs/built-with-ai/BUILT-WITH-AI.md") else "docs/BUILT-WITH-AI.md"
rewrite(bwa, [(r"\d+ Vitest tests at \d+\.\d+\.\d+\.\d+", f"{n('vitest')} Vitest tests at {ver}")])
if gate_count:
    rewrite(bwa, [(r"(gate: )\d+( xUnit tests)", lambda m: f"{m.group(1)}{n('xunit-sqlite')}{m.group(2)}")])
print(f"README results: {ver}, {total:,} runs, Vitest {n('vitest')}, declared browser {declared}")
