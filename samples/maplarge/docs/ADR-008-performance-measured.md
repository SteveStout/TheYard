# ADR: Performance, measured

Status: accepted, 2026-09-29.

## Context

The brief: "performance is highly valued." A claim about performance is worth what its
measurement is worth, so this record is the measurement and the choices behind it, and a test
prints the numbers on every run so the next reader can compare.

## Decision

**Enumerate, do not stat.** `PhysicalFileStore` walks a folder with
`DirectoryInfo.EnumerateFileSystemInfos`, which hands back each entry with its attributes and size
already read from the directory listing. A folder of ten thousand files is one enumeration, not
ten thousand `stat` calls, and hidden and system entries are skipped by the enumeration options
rather than filtered afterwards.

**Search walks lazily and stops at a cap.** `Descendants` is an `IEnumerable` over a recursive
enumeration with `IgnoreInaccessible`; `FileBrowser.Search` reads it until it has `limit` matches
(200 unless asked, 1,000 at most) and then stops, so a search that fills its limit in the first
subfolder never reads the rest of the tree. The reply says `truncated: true` so the page can say
"narrow the search" instead of pretending the list is complete. The cap is the honest choice: an
uncapped search of a large tree is the one request in this API that could hold a thread for
seconds.

**Streams for bytes.** An upload is copied from the request to a `FileStream` opened asynchronous;
a download is `PhysicalFile` with range processing, so a paused download resumes and a large one
never sits in memory. The size limit is checked against the declared length before a byte is read,
so a refused upload costs nothing.

**Every browse and search reports `took_ms`**, measured around the store call, and the page shows
it in the corner. The number is the server's own, not the round trip.

**The page does its share.** Listings are cached by path for the life of the page so Back is
instant; a write forgets only the paths under it. The table is rebuilt as one fragment; one
listener serves every row; the search box waits 250 ms after the last keystroke before asking.

## The measurement

`PerformanceTests` generates 100 folders of 100 files under temp, warms the directory cache with
one read, and times the use cases over the real disk adapter. On the machine that built this
(a four-core laptop with a virus scanner, Windows 11, 2026-09-29; generating the 10,000 files
took 6.9 s, which says what the disk is):

| What | Result |
| --- | --- |
| Browse one folder of 100 files | under 1 ms |
| Browse the root of 100 folders | 2 ms |
| Search 10,000 files, capped at 200 matches | 6 ms |
| Search the whole tree for one name (10,000 entries read) | 47 ms |

The bars the test holds are looser than the numbers (250, 500 and 3,000 ms), because the test runs
on whatever machine runs it and a bar that fails on a slow build agent is a bar nobody keeps. The
numbers are what to compare against.

## What it cost

A capped search means a search can be incomplete, and the page has to say so. It does. The
alternative, paging a search, would double the API for a case the cap already handles: a person
who sees "stopped at the limit" types one more character.

## Addendum, 2026-10-03: downloads come through the store

A download no longer uses `PhysicalFile`. `FileBrowser.Download` asks the store for the file with `IFileStore.OpenRead`, and the controller sends that stream with range processing on. The bytes still go out a piece at a time and a paused download still resumes, and a store that is not the disk could serve downloads too.

## Addendum, 2026-10-03: measured again

The same test in the 1.0.0.30 gate, on the same laptop: generating the files took 6.5 s, browsing one folder and the root each under 1 ms, the capped search 8 ms, and the whole-tree search 44 ms. The Shed, explained quotes these.

## Where it sits

The speed lives in two rings: Infrastructure walks a folder in one pass, and Application stops a search as soon as it has enough matches. It follows the L in SOLID: IFileStore promises to hand entries back one at a time, so any store that stands in for the disk keeps a search fast as long as it keeps that promise. The cost is that a search can stop early, and the page has to say the list was cut off. If people needed every match in a huge tree, the search would hand back its results a page at a time.

## Files

- [`Infrastructure/PhysicalFileStore.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Infrastructure/PhysicalFileStore.cs): the walk.
- [`Application/FileBrowser.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/Application/FileBrowser.cs): the capped search.
- [`tests/TestProject.Tests/PerformanceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/TestProject.Tests/PerformanceTests.cs): the measurement.

The walk:

```live path=Infrastructure/PhysicalFileStore.cs region=walk
```

The capped search:

```live path=Application/FileBrowser.cs region=search
```

The test that prints the numbers:

```live path=tests/TestProject.Tests/PerformanceTests.cs region=*
```
