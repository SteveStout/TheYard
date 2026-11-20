#!/usr/bin/env bash
# fill_version.sh
#
# Does:      Fills the numbers a lane ships at the moment it claims, under the gate lock (ADR-031, the addendum on the
#            fill at the claim), on its one commit on top of origin/main: the version is origin/main's changelog top
#            line plus one in its fourth number (or the owner's move of the third, only when OWNER_YES names it, as
#            versions.sh allows), the date is today in America/Chicago, and the record number is one above the highest
#            docs/decisions/ADR-NNN-*.md on origin/main. Every placeholder for the three is replaced in tracked files
#            and in the commit message, and a file whose name holds the record's placeholder is renamed; the record
#            count in words and the Playwright count are written into README.md, docs/about/AI-DEVELOPMENT.md and
#            docs/built-with-ai/BUILT-WITH-AI.md from the tree (ADR-137, the addendum on the counts); the version
#            card is redrawn; the quality ratchet is tightened to what .github/quality.py measures, a number only ever
#            moving the good way; the commit is amended. Prints VERSION=<v> ADR=<n or none> HEAD=<sha8>. Run twice,
#            the second run changes nothing.
# Does not:  Take or read the gate lock (scripts/gate_lock.sh claim takes it first), rebase, push, or run a check
#            (scripts/preflight.sh runs next). Touch a change not yet committed: it refuses a dirty tree. Renumber a
#            commit already filled for another version: it refuses and says so, because the old number's words are
#            no longer placeholders it can find.
# Used by:   Whoever ships, right after scripts/gate_lock.sh claim (CLAUDE.md, docs/ci-cd/CICD.md).
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
refuse() {
  echo "fill REFUSED: $*"
  exit 1
}
# shellcheck source=scripts/versions.sh
. scripts/versions.sh

# The placeholders, each spelled in two parts, so the fill never rewrites this file.
ver_mark="@""VER@"
date_mark="@""DATE@"
adr_mark="@""ADR@"

# #region the-commit
# One commit on the main that was just read, and nothing beside it: the amend folds in only what this script wrote.
git diff --quiet HEAD -- || refuse "the tree has changes that are not committed: fold them into the one commit, or stash them"
git fetch -q origin +refs/heads/main:refs/remotes/origin/main || refuse "origin could not be read"
[ "$(git rev-parse -q --verify HEAD~1 || true)" = "$(git rev-parse origin/main)" ] ||
  refuse "HEAD is not exactly one commit on origin/main: rebase onto origin/main first"
# #endregion the-commit

# #region the-numbers
main=$(git show origin/main:docs/CHANGELOG.md | versions | sed -n 1p)
[ -n "$main" ] || refuse "origin/main's changelog names no version"
version=$(next_version "$main")
if [ -n "${OWNER_YES:-}" ]; then
  IFS=. read -r a b c _ <<< "$main"
  [ "$OWNER_YES" = "$a.$b.$((c + 1)).0" ] || refuse "OWNER_YES names $OWNER_YES and the owner's move from main's $main is $a.$b.$((c + 1)).0"
  version=$OWNER_YES
fi
day=$(TZ=America/Chicago date +%Y-%m-%d)
highest=$(git ls-tree --name-only origin/main docs/decisions/ | sed -n 's#^docs/decisions/ADR-\([0-9][0-9]*\)-.*\.md$#\1#p' | sort -n | tail -n 1)
record=$(printf '%03d' $((10#${highest:-0} + 1)))
# The top entry is the placeholder, or this version on a second run. Anything else was filled for another number.
first=$(grep -m1 -E '^- \*\*[^*]+\*\*' docs/CHANGELOG.md | sed 's/^- \*\*\([^*]*\)\*\*.*/\1/' || true)
[ "$first" = "$ver_mark" ] || [ "$first" = "$version" ] ||
  refuse "the changelog's top line is '$first' and it must be the version placeholder or $version, main's $main plus one: a version ships with its own line, filled here"
# #endregion the-numbers

# #region the-fill
adr=none
while IFS= read -r -d '' path; do
  git mv -- "$path" "${path//"$adr_mark"/$record}"
  adr=$record
done < <(git ls-files -z | grep -zF -- "$adr_mark" || true)
if git grep -qIF -e "$adr_mark"; then adr=$record; fi
while IFS= read -r -d '' path; do
  sed -i "s/$ver_mark/$version/g; s/$date_mark/$day/g; s/$adr_mark/$record/g" -- "$path"
done < <(git grep -lzIF -e "$ver_mark" -e "$date_mark" -e "$adr_mark" || true)
message=$(git log -1 --format=%B)
case "$message" in *"$adr_mark"*) adr=$record ;; esac
filled=${message//"$ver_mark"/$version}
filled=${filled//"$date_mark"/$day}
filled=${filled//"$adr_mark"/$record}
# #endregion the-fill

# #region the-counts
# The counts the public face states are written here, on the tree that is being numbered, so no lane edits them and two
# versions never conflict on them at the cop's rebase (ADR-137, the addendum on the counts): the record count in words,
# from the records this commit carries, and the Playwright count, from the specs' `test(` lines, read the way
# PublicFaceTests reads both. A file or a claim the tree does not have is left alone; the tests say when one is missing.
python3 - << 'PY' || refuse "the counts could not be written (python3, README.md and the two documents beside it)"
import glob, os, re, subprocess

units = "zero one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen".split()
tens = "- - twenty thirty forty fifty sixty seventy eighty ninety".split()
def words(n):
    if n < 20:
        return units[n]
    if n < 100:
        return tens[n // 10] + ("-" + units[n % 10] if n % 10 else "")
    return units[n // 100] + " hundred" + (" and " + words(n % 100) if n % 100 else "")
numbers = {words(n): n for n in range(1000)}
numbers["hundred"] = 100

listed = subprocess.run(["git", "ls-files", "docs/decisions/"], capture_output=True, text=True, check=True).stdout.split()
records = sum(1 for path in listed if re.fullmatch(r"docs/decisions/ADR-\d+-.*\.md", path))
specs = sorted(glob.glob("tests/e2e/*.spec.ts"))
declared = sum(1 for path in specs for line in open(path, encoding="utf-8") if re.match(r"^\s*test\(", line))

claim = re.compile(r"\b([A-Za-z][a-z]+\s+hundred(?:\s+and\s+[A-Za-z][a-z]+(?:-[a-z]+)?)?|[A-Za-z][a-z]+(?:-[a-z]+)?)(\s+decision\s+records?)\b")
def recount(match):
    said = re.sub(r"\s+", " ", match.group(1))
    if said.lower() not in numbers or records == 0:
        return match.group(0)
    now = words(records)
    return (now[0].upper() + now[1:] if said[0].isupper() else now) + match.group(2)

for path in ("README.md", "docs/about/AI-DEVELOPMENT.md", "docs/built-with-ai/BUILT-WITH-AI.md"):
    if not os.path.exists(path):
        continue
    text = open(path, encoding="utf-8").read()
    after = claim.sub(recount, text)
    if specs and path != "docs/about/AI-DEVELOPMENT.md":
        after = re.sub(r"\d+( Playwright tests)", lambda m: f"{declared}{m.group(1)}", after)
        after = re.sub(r"(\| End to end \| Playwright \| )\d+( declared \|)", lambda m: f"{m.group(1)}{declared}{m.group(2)}", after)
    if after != text:
        open(path, "w", encoding="utf-8", newline="\n").write(after)
PY
# #endregion the-counts

# #region the-card-and-the-ratchet
# The version card names the version, so it is drawn after the fill. The picture is drawn from the drawing: when the
# drawing came out the same, the picture is put back as committed, so a second run changes nothing.
if [ -f docs/images/og.mjs ]; then
  node docs/images/og.mjs > /dev/null || refuse "the version card could not be drawn (node docs/images/og.mjs)"
  if git diff --quiet HEAD -- docs/images/og.svg; then git checkout -q HEAD -- public/og.png 2> /dev/null || true; fi
fi
# A limit only ever moves the good way: down, or up for a number whose name says higher is better.
if [ -f .github/quality.py ] && [ -f .github/quality-ratchet.json ]; then
  python3 - << 'PY' || refuse "the quality ratchet could not be measured (python3 .github/quality.py .)"
import json, subprocess
path = ".github/quality-ratchet.json"
with open(path, encoding="utf-8") as f:
    limits = json.load(f)
scan = subprocess.run(["python3", ".github/quality.py", "."], capture_output=True, text=True, check=True)
measured = json.loads(scan.stdout)["ratchet"]
moved = False
for key, limit in limits.items():
    now = measured.get(key)
    if now is None:
        continue
    if (now > limit) if key.endswith("higher_is_better") else (now < limit):
        limits[key] = now
        moved = True
if moved:
    with open(path, "w", encoding="utf-8") as f:
        f.write(json.dumps(limits, indent=1) + "\n")
PY
fi
# #endregion the-card-and-the-ratchet

# #region the-amend
git add -u
if git diff --cached --quiet HEAD && [ "$filled" = "$message" ]; then
  echo "nothing to fill: the commit already names $version"
else
  printf '%s\n' "$filled" | git commit -q --amend -F -
fi
echo "VERSION=$version ADR=$adr HEAD=$(git rev-parse --short=8 HEAD)"
# #endregion the-amend
