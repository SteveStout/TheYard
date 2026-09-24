# ADR: Every page, checked at every roll

Status: accepted, 2026-09-19, shipped as 1.0.0.148. Written the morning Steve opened the README
inside the app and found it broken, an hour after a link to one of its pages went into a LinkedIn
post.

## Context

The README linked two documents at `/api/docs/built-with-ai` and `/api/docs/performance`. Those are
the addresses the documents dialog fetches markdown from, so a reader who followed either one got a
page of raw markdown in a new tab. Both answered 200. Both had answered 200 for weeks.

That is the whole lesson of this record: **every check this project had asked whether an address
answers, and none asked what it answered with.** `RecordLinksTests` proved every repository link
pointed at a file that exists. The after-ship reader proved both domains served the new version and
that four addresses came back 200. A link that returns a wall of markdown to a human being passes
both.

Two more holes sat beside it. The container had no idea what it was serving: a document whose file
was renamed, a drawing whose SVG went missing, a static file the build stopped copying would each
have been found by a visitor rather than by the site. And the check that did exist ran from outside,
after a ship, against four addresses chosen by hand, in a script on one machine, which is a check
that runs when somebody remembers to run it.

## What was considered

| Option | What it buys | What it costs |
| --- | --- | --- |
| **The container sweeps itself at every roll** | Every address the site serves, checked by the thing that serves them, a second after it starts; nothing to remember and nothing to pay | The reading is the origin's, not the edge's, and it has to be kept out of the traffic it would otherwise pollute |
| An uptime service | A check from the outside, on a schedule, with alerting | A tier on a bill this project keeps at the list price of two containers, and a hand-kept list of addresses in somebody else's console |
| More addresses in the after-ship reader | One more line in a script that already runs | Still a hand-kept list, still on one machine, still nothing the site itself knows |
| A test that fetches every address in the gate | Catches it before the ship, which is where a defect is cheapest | It proves the checkout is sound, never that what rolled is sound; this record takes both |

**Decision: the container checks itself, at every roll and on demand, and the gate checks the same
list.** The sweep lives in `PageStatus.cs`: `ServedAddresses.All()` builds the list, the runner asks
for each address four at a time, and the report is what the Admin tab shows.

## The three rules that make it worth having

**The list is derived, never kept.** Every document and every drawing comes out of `DocsCatalog`,
which is the same dictionary the server serves from and the sidebar is held to. A document added
tomorrow is swept tomorrow with nothing to remember. Only the addresses that are not documents are
written out: the app, the API's front pages, and the three files the build copies to the root of the
domain.

**Nothing is called down that was never served here.** The app, `robots.txt`, `sitemap.xml` and the
preview card come out of the frontend build, which the image has and a checkout does not: on a
developer's machine the API runs behind the dev server and those four addresses are not its to
answer. The sweep checks them where the web root holds them and names them nowhere else, because a
red reading that means "you are running the API on its own" teaches a reader to ignore the card.

**An endpoint that throws is a page that is down.** The first list left the Admin tab's own readings
out, on the grounds that they are readings rather than pages. On 19 September
`/api/admin/machines` answered 500 on both live sites for four minutes and the sweep was green
through all of it, because nothing was asking. The readings are in the list now.

**An answer is judged, not counted.** An address is up when it answers 200 **with bytes in it**, and
the report records the content type beside the status, so a document served as anything but markdown
and a drawing served as anything but a page are visible on the card and fail the gate. That is the
check the README needed.

**A sweep is not traffic.** Every request the sweep makes carries `X-Yard-Page-Check`, and the
request hook drops anything carrying it. Ninety self-requests at every roll would otherwise push a
morning of real traffic out of a five-hundred slot ring and add a visitor to the activity card who
is this container.

## What the browser suite found, and it is the useful half of this record

The first cut read the container's own address out of the variable the proof fills in a callback on
`ApplicationStarted`. It was always null, so the roll never swept anything, and the endpoint answered
"not run" on a container that had been up for a minute. The reason is that a `CancellationToken`
invokes its callbacks in the reverse of the order they were registered: this sweep, registered last
in the file, ran first, before the address had been read.

The fix is not to register the two in the other order, which would work until somebody moved a line.
`SelfAddress` asks the server what it is listening on at the moment the sweep runs, and turns a
wildcard or a `localhost` into the loopback. The gate's browser pass is what caught it, on a card
that said in plain words that no check had run.

## What it does not claim

The sweep dials this container's own loopback, so it reports what the container serves. It does not
prove what the edge is serving the public: a cached page at the edge and a broken origin look
different from outside, which is why the after-ship reader still reads both public domains from a
machine that is not this one. The card says which of the two it is showing. Nothing in this record
adds a resource, a tier or an edge feature to the bill.

## Addendum, 2026-09-23 (1.0.3.8): a second look, and a second sweep

Steve, 12:0x CDT: "we have 3 reported pages down, focus on that ASAP." The roll onto 1.0.3.6 had swept 121 of 123 on the SQL site and 120 of 123 on the Cosmos DB site: `/api/health`, `/api/admin/machines` and `/api/admin/kept?card=errors&window=24h` each past the client's thirty seconds with `TaskCanceledException`, on both containers, in the same minute, and the Pages tile said "3 down, needs attention" until the next roll. Every one of the three answered when asked again a minute later. The roll's sweep runs in the busiest second the process has: `ApplicationStarted`, four addresses at once, while both containers warm a hundred thousand vehicles on the one core they share and ask the same Basic database for its resource view.

Two things, both mechanical. **A second look:** an address that did not answer on the first pass is asked again, alone, once the pass is over, and an address that answers then is up, with "answered on a second look" as its reason, so the card still says it was slow the first time. **A second sweep:** three minutes after the roll's sweep the settled process sweeps itself again (`PageStatusRunner.SecondSweep`, trigger "settled"), and that is the reading the tile shows until somebody asks for another. `PageStatusTests` holds both: a handler that times out once is up on the second look with its reason, one that times out twice is down with its reason, and the second sweep's delay. The ship script reads the Admin section on both sites after every roll now (`staging\polishlane\admin-after-roll.py`): health, pages, machines, kept, metrics, and it asks for a sweep itself when the roll's reading has anything down, so the after-ship log carries the settled reading and not the start's.

## Files

- [`api/TheYard.Api/PageStatus.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/PageStatus.cs): the derived list, the sweep, and what it reports.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the sweep at startup, the two endpoints, and the request hook that drops its header.
- [`api/TheYard.Api/DocsCatalog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/DocsCatalog.cs): the dictionary the list is built from.
- [`src/components/admin/PagesCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/admin/PagesCard.tsx): the card, failures first.
- [`api/TheYard.Tests/PageStatusTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/PageStatusTests.cs): the list against the catalogue, every address answering, the types, and the header that keeps a sweep out of the ring.
- [`api/TheYard.Tests/RecordLinksTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/RecordLinksTests.cs): the rule that put the README's links right, shipped as 1.0.0.147.
- [`docs/ADR-075-the-rules-a-change-has-to-pass.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-075-the-rules-a-change-has-to-pass.md): the rules table this record adds two rows to.
