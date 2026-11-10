#!/usr/bin/env python3
"""coderabbit_rules.py [--write]

Does:      Writes the path instructions in .coderabbit.yaml from ADR-075's rule table (ADR-129, the addendum of
           1.0.5.5), so CodeRabbit is told each rule in its record's own words: every instruction is clauses quoted
           verbatim from ADR-075 rows that name the record and the test that holds the rule, then the scope that
           test reads (its folders and what it leaves out), then the record. With no argument it checks: every quoted
           clause is still in such a row, and the configuration holds exactly what this would write; it exits 1 and
           says which instruction drifted when either fails. With --write it rewrites the configuration's
           path-instructions region.
Does not:  Choose a rule, a level or a path on its own: the seven rules are ADR-129's table, and which folders each
           test reads is held against the test itself by CodeRabbitRuleTests. Touch anything outside the region.
Used by:   CodeRabbitRuleTests (the check), and whoever changes a rule's row in ADR-075 or its test's scope.
"""
import re
import sys
import textwrap
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CONFIG = ROOT / ".coderabbit.yaml"
START = "  path_instructions:\n"
END = "  # #endregion path-instructions"

SITE_RINGS = "api/TheYard.Data,api/TheYard.Domain,api/TheYard.Application,api/TheYard.Infrastructure,api/TheYard.Infrastructure.Cosmos,api/TheYard.Api"

# #region the-rules
# One entry per instruction; a record may need two, one per scope, and ADR-129's table still lists it once. Each
# clause is a verbatim span of an ADR-075 row whose record cell names the record (by number or by title) and whose
# test cell names the test.
RULES = [
    {
        "record": "ADR-122",
        "test": "CancellationTests",
        "path": "{api,functions,server,samples/theshed}/**/*.cs",
        "clauses": [
            "Every async method in production C# takes a CancellationToken but the one framework override whose signature this codebase does not own",
            "every timer the job hunt Function runs declares the token Azure hands it and passes it on",
        ],
        "scope": "Production C# only, as the ratchet's count reads it: a file in a test project (a path with Tests in it or a tests/ folder) and the Experiment console tool are out of scope, and so are methods named Main or Dispose; an async test method with no token breaks nothing.",
    },
    {
        "record": "ADR-028",
        "test": "HouseVoiceTests",
        "path": "**/*",
        "clauses": ["Nothing this repository ships or serves contains an em dash"],
        "scope": "That is the character U+2014 in code, comments, documents or data, and its two HTML spellings, the named entity mdash and the numbered reference 8212; use a comma, a colon or two sentences.",
    },
    {
        "record": "ADR-074",
        "test": "ProductionCommentTests",
        "path": "{" + SITE_RINGS + ",src}/**/*.{cs,ts,tsx,css}",
        "clauses": [
            "No production comment leans on a version number, a date or a review to stand in for its reason",
            "In the site's own code a production comment names no month, no written date, no review by its name and no person by first name; a format example in quotes or backticks is not history",
        ],
        "scope": "Production comments only: a file under a Migrations folder, a file whose name has .test. in it and any test project are out of scope.",
    },
    {
        "record": "ADR-074",
        "test": "ProductionCommentTests",
        "path": "functions/**/*.{cs,ts,tsx,css}",
        "clauses": [
            "No production comment leans on a version number, a date or a review to stand in for its reason",
            "In the job hunt's Function a comment explains by no date, no version number, no named review and nobody's ask, while the owner's name and a month name there are the subject the code acts on and not history",
        ],
        "scope": "Production comments only: the job hunt's test project, functions/jobhunt.Tests, is out of scope.",
    },
    {
        "record": "ADR-088",
        "test": "OnionTests",
        "path": "{" + SITE_RINGS + "}/**/*.cs",
        "clauses": [
            "Dependencies point inward: Data and Domain use nothing outside .NET's base class library, Application uses only Domain and Data, and the document store adapter borrows only the shared user from the relational one",
            "Endpoints never reach a store or the service container, reach the auction only through Application, the host never queries a database itself, and services are registered only in the composition root",
            "Domain never reads a file, calls the network or reads the system clock or a random number itself, and Application never reads a file or calls the network",
            "Data has no behaviour (accessors, constructors and what the compiler writes for a record only), Data and Domain read neither the disk, the network nor the machine (a clock, a process, a type found by name), and an adapter neither uses the web beyond Identity nor decides on money",
        ],
        "scope": "The site's six onion projects only; tests, the migrations and the Experiment tool are out of scope.",
    },
    {
        "record": "ADR-107",
        "test": "SliceTests",
        "path": "{api,functions}/**/{Features,Shared}/**/*.cs",
        "clauses": [
            "a type under Features/<Slice>/ in Domain, Application, either adapter or the host uses only its own slice and the Shared concerns in any ring, never another slice's type in any ring (the host's Admin sub-slices are the one Admin slice to the inner rings)",
            "The job hunt Function's slices: a type under a ring's Features/<Slice>/ uses only its own slice and the Shared concerns in any ring, shared code uses no slice",
        ],
        "scope": "The site's rings and the job hunt Function's rings; test projects are out of scope.",
    },
    {
        "record": "ADR-115",
        "test": "CqrsTests",
        "path": "{api/TheYard.Application,functions/JobHunt.Application}/**/*.cs",
        "clauses": [
            "Every request is a sealed `<Verb><Noun>Query` or `<Verb><Noun>Command` with exactly one handler implementing IQueryHandler or ICommandHandler and named for it; a query handler takes no store and no writer",
            "A query handler is handed no port that can write: no constructor parameter among this codebase's types has a member, counting every inherited interface, whose name starts with Keep, Write, Save, Upsert, Delete, Add, Remove, Record, Put or Set",
            "a use case that only reads is handed the reading half of a split port",
        ],
        "scope": "The site's Application ring and the job hunt Function's; test projects are out of scope.",
    },
    {
        "record": "ADR-075",
        "test": "FileShapeTests",
        "path": "{api,src,render,functions,server}/**/*.{cs,ts,tsx}",
        "clauses": [
            "No production C# file under `api/` and no TypeScript file under `src/` runs past 300 lines; a longer file is split by job and the file that keeps the name lists its parts (tests, migrations and generated files are left out)",
            "no C# type under `api/` runs past 300 lines either, counted across all of its partial files",
        ],
        "scope": "The file rule reads api/, src/ and render/, and the job hunt's suite and the machine lanes' suite read functions/ and server/ the same way; the type rule reads api/ only. Out of scope: every test project (TheYard.Tests, jobhunt.Tests, JobHunt.Server.Tests), the SQLite migrations project, files named *.test.ts or *.test.tsx, and generated files (*.d.ts, *.Designer.cs, *ModelSnapshot.cs); a test file over 300 lines breaks nothing.",
    },
]
# #endregion the-rules


def text(rule):
    """One instruction's words: the quoted clauses as sentences, the test's scope, the record."""
    said = " ".join(c[0].upper() + c[1:] + "." for c in rule["clauses"])
    return f"{said} {rule['scope']} ({rule['record']})."


def region():
    lines = [START]
    for rule in RULES:
        lines.append(f'    - path: "{rule["path"]}"\n')
        lines.append("      instructions: >-\n")
        for line in textwrap.wrap(text(rule), width=108, break_on_hyphens=False, break_long_words=False):
            lines.append(f"        {line}\n")
    return "".join(lines)


def table_rows():
    """ADR-075's rule table: (rule, record, tests) for every row."""
    adr = next((ROOT / "docs" / "decisions").glob("ADR-075-*.md")).read_text(encoding="utf-8")
    return [tuple(cell.strip() for cell in m.groups()) for m in re.finditer(r"^\| (.+?) \| (.+?) \| (.+?) \|$", adr, re.M)]


def title(record):
    found = list((ROOT / "docs" / "decisions").glob(f"{record}-*.md"))
    return found[0].read_text(encoding="utf-8").splitlines()[0].removeprefix("# ").strip() if found else None


def drift():
    """Every way the configuration has left its records, one line each; empty when it holds."""
    wrong = []
    rows = table_rows()
    for rule in RULES:
        name = title(rule["record"])
        mine = [r for r in rows if (rule["record"] in r[1] or (name and name in r[1])) and rule["test"] in r[2]]
        if not mine:
            wrong.append(f"{rule['record']} {rule['path']}: ADR-075 has no row naming {rule['record']} and {rule['test']}")
        for clause in rule["clauses"]:
            if not any(clause in r[0] for r in mine):
                wrong.append(f"{rule['record']} {rule['path']}: ADR-075's rows for {rule['test']} no longer say: {clause}")
    config = CONFIG.read_text(encoding="utf-8")
    start, end = config.find(START), config.find(END)
    if start < 0 or end < start:
        wrong.append(".coderabbit.yaml has no path-instructions region")
    elif config[start:end] != region():
        wrong.append(".coderabbit.yaml's path instructions are not what scripts/coderabbit_rules.py writes: run it with --write")
    return wrong


def main(argv):
    if len(argv) > 1 and argv[1] == "--write":
        config = CONFIG.read_text(encoding="utf-8")
        start, end = config.find(START), config.find(END)
        CONFIG.write_text(config[:start] + region() + config[end:], encoding="utf-8")
    wrong = drift()
    for line in wrong:
        print(line)
    print("the instructions match their records" if not wrong else f"{len(wrong)} drifted")
    return 1 if wrong else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
