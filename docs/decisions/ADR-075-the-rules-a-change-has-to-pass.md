# ADR: The rules a change has to pass

Status: accepted, 2026-09-15, shipped as 1.0.0.136.

## In plain words

Every rule this project lives by that a machine can check is listed here, next to the test that checks it, and another test checks the list itself. Break a rule and the build fails and names it.

What that is worth: a new developer finds a rule before breaking it, not after, and the organization keeps its standards when people come and go, because the rules live in the build instead of in someone's head.

## Context

Seventy-five records hold the decisions this project has made, and most of the standing ones are already
enforced by a test rather than by memory. Nothing said which test. A reader who wanted to change
something had two ways to find the rule that governed it: read all seventy-five records, or break one and
read the failure that came back. The first is not a thing anybody does, and the second is the reason a
rule gets discovered at the wrong moment.

There is a second problem, and it is the one that prompted this. The record set has a shape: a title the
index can read, a status line saying what became of the decision, and a Files section pointing at the
code it decided about. That shape was held by habit, and habit lasted until the seventy-fourth record,
written thirteen days after the convention settled, which opened with a bold status line in a date format
no other record uses. Nothing failed, because nothing was looking.

## Decision

The rules that a machine can check are listed here beside the test that checks them, and the list itself
is checked. A rule with no test in this table is a rule the next change will break.

## The rules, and what holds each one

| The rule | Where it was decided | What holds it |
| --- | --- | --- |
| Record numbers run from one with no gap, and every record the sidebar offers has a file | ADR: The Decision Records index | RecordLinksTests |
| Every repository link in every document points at something that exists | ADR: Docs and testing | RecordLinksTests |
| Every record a comment cites, by name or by number, is a record that exists | ADR: Docs and testing | RecordLinksTests |
| Every link from a document to this site opens a page: a document as `?doc=` with a slug the catalogue serves, a drawing the catalogue draws, and no bare local address in prose | ADR: Docs and testing | RecordLinksTests |
| Every address this site serves answers with something, and a document is served as markdown | ADR: Every page, checked at every roll | PageStatusTests |
| The sweep checks every document and every drawing the catalogue serves, and its own requests are not counted as traffic | ADR: Every page, checked at every roll | PageStatusTests |
| The container samples its own memory and processor share, and each store's reading is the one that store actually keeps | ADR: What the machines are doing | MachinesTests |
| The request ring folds into minutes, each with its own median and its own errors, and the hour and the month are drawn from the one shape | ADR: The Admin tab, as a product | MachinesTests |
| A public ring hands on what it takes and not what it refuses, a kept entry is the entry the ring serves with the spine's private fields empty, it outlives the widest window that reads it, and the keyed log shows none of them | ADR: Logs that outlive the container | KeptRingTests |
| A kept minute leaves out a figure nobody read, a window is folded into the buckets it is drawn in, and the store's grouped query answers what the folding does | ADR: What the machines are doing | MachineHistoryTests |
| An identity token is asked for at whichever door the host has, and a web app's card claims no restart count it was never given | ADR: One plan, two sites | AzureSelfTests |
| Every setting a container group carried is a setting the two sites carry, and nothing deploys a template in complete mode | ADR: One plan, two sites | AppServiceTemplateTests |
| The living documents that describe what runs name the plan size the template declares | ADR: A rendering service beside the API | AppServiceTemplateTests |
| The rendering service names the session cookie the API issues, so the edge's copy of a page drawn for nobody is never handed to a signed-in visitor | ADR: A rendering service beside the API | CacheHeaderTests |
| A request on a store whose catalogue is cold waits for the load without blocking a thread, and a warm store is waited on for no time at all | ADR: One container, both stores | WarmthTests |
| No production comment leans on a version number, a date or a review to stand in for its reason | ADR: Code that reads like code | HouseVoiceTests |
| A production comment names no month, no written date, no review by its name and no person by first name; a format example in quotes or backticks is not history | ADR: Code that reads like code | HouseVoiceTests |
| Every record opens with its title, says what became of it, says where it sits in the rings above its Files section, and has a Files section with at least one link | This record, ADR: Onion and SOLID, how this codebase holds them | RecordShapeTests |
| Every rule in this table names a test that exists, and every record it cites exists | This record | RuleTableTests |
| Dependencies point inward: Data and Domain use nothing outside .NET's base class library, Application uses only Domain and Data, and the document store adapter borrows only the shared user from the relational one | ADR: Onion and SOLID, how this codebase holds them | OnionTests |
| Endpoints never reach a store or the service container, reach the auction only through Application, the host never queries a database itself, and services are registered only in the composition root | ADR: Onion and SOLID, how this codebase holds them | OnionTests |
| Domain never reads a file, calls the network or reads the system clock or a random number itself, and Application never reads a file or calls the network | ADR: Onion and SOLID, how this codebase holds them | OnionTests |
| Every package the build uses has a row in the versions record with the version in use, and a row that is behind says why | ADR: Technology versions | TechnologyVersionsTests |
| Code shown in a document is read from the build at request time, never pasted | ADR: Live code samples | LiveSamplesTests, LiveSampleCoverageTests |
| The record count on every living document, and the README's xUnit and Playwright counts, are the counts the build declares | ADR: The public face | PublicFaceTests |
| The sitemap lists every document the catalogue serves and nothing else, and llms.txt links only to what the site serves | ADR: The public face | PublicFaceTests |
| The Cloud infrastructure drawing names every card the rail lists under it | ADR: One frame for every resource | CloudDrawingTests |
| /about is a served page with its own head, its Person names only his name, title, profiles and city, the sitemap lists it with the build's day, and the verification tag is written only when it is given | ADR: Every diagram opens on its own page, ADR: The public face | DiagramPageTests, PublicFaceTests, DockerBuildInputsTests |
| The slug, the catalog and the sidebar offer the same documents | ADR: The staff review | DocumentationCatalogTests |
| Every document sits in the folder of the sidebar section that offers it, the records in `docs/decisions/`, and the root of `docs/` holds only those folders, the changelog and the pictures | ADR: One folder per sidebar section | DocsFolderTests |
| Every file in `src/app` and `src/library`, and every stylesheet under `src`, opens with what it does, what it does not, and which files use it; the last is read against the files that import it, and no file there runs past 300 lines but the ones named with why | ADR: The React configuration, explained for a new developer | FileHeaderTests |
| No production C# file under `api/` and no TypeScript file under `src/` runs past 300 lines; a longer file is split by job and the file that keeps the name lists its parts (tests, migrations and generated files are left out). From 1.0.4.47 no C# type under `api/` runs past 300 lines either, counted across all of its partial files. From 1.0.4.70 the file rule is one body, tests/rules/LengthRule.cs, that the job hunt's suite runs over `functions/` too | ADR: The rules a change has to pass | FileShapeTests, RingTests |
| The five Style pages are served and are the Style section's rows, say "design token" and never a bare "token" (as do the design token files' comments), carry no em dash, and every live number and live fence on them is one the build can count or read, and every tile and glossary link lands on a real section | ADR: The palette | StyleSectionTests |
| One changelog line per shipped version, newest first, and the deploy reads the version from it | ADR: The changelog, ADR: The version comes from the changelog | ChangelogTests |
| Nothing this repository ships or serves contains an em dash | ADR: Style, enforced | HouseVoiceTests |
| No marker for work that is not happening, no focused test, no console call under `src` | ADR: Broken windows, and the rule that answers them | BrokenWindowsTests |
| Every class is sealed, static, abstract or inherited, and the analyzer that catches the internal half is a warning | Best Practices, Sealed by default | SealedByDefaultTests |
| A pinned package version is a decision, and a build error is not a reason to move it | The incident at 1.0.0.91, in the changelog | PackagePinTests |
| Both deploy workflows answer to one set of constants | ADR: A permanent address for the second site | DeployWorkflowTests |
| The SQL project in source control is the authority for the relational schema | ADR: Data first, and the database in source control | SchemaConformanceTests |
| The server owns the clock and every derived fact, and no request names a day | ADR: Three readers with no memory of the project | AuctionScheduleTests |
| Sold is decided before every other bid rule, for everybody | ADR: Accounts and per-user bids | BidRulesTests |
| Every public endpoint is in the API document with an operation id, a summary, its responses and its lock, and no operator endpoint is | ADR: The API describes itself | ApiDocumentTests |
| The first screen's one reading answers every field the band draws, and its latency figure is the request-weighted middle five minutes of the day, null when the day is empty | ADR: One reading for the first screen | PulseTests |
| A link into a section of another page names a slug the site serves and a second-level heading on it, which is the only kind the site gives an id | ADR: The staff review | DocumentationCatalogTests |
| What colleagues say comes from one file in one shape: every entry the same seven fields and nothing else, the month in words, the pull sentence as written, no two from one person, and the endpoint answers the file in its order | ADR: The sidebar | RecommendationsTests |
| No stylesheet or component carries a raw colour; every colour is a token in the one sheet | ADR: The palette | StyleRulesTests |
| Every hex on the Style pages (Colour and style, Background and ribbon) is a token's value, every colour token is on one of them, and every contrast figure they state is the figure the tokens give and clears its bar | ADR: The palette | StyleRulesTests |
| A chart series is never a status colour, and a line takes a status tone only for server errors | ADR: The Admin tab, as a product | StyleRulesTests |
| Gold is trim: only the header, the brand mark and the named trim use it, never a chart's line, a tile or a ring | ADR: The palette | StyleRulesTests |
| There is one header gradient, defined once, and every header bar uses it | ADR: The palette | StyleRulesTests |
| Every page title's underline and every stat tile's top rule is the gold bar, drawn from `--gradient-gold` with `border-image` | ADR: The palette | StyleRulesTests |
| Nothing that holds a word or an image is faded, a quiet word is never on the bare ground, and the browser suite and the token test that hold those are still there | ADR: The glass look | StyleRulesTests |
| One face for the whole site, IBM Plex Sans, with tabular figures set once on the body, and a monospaced face only on code | ADR: The palette | StyleRulesTests |
| Every panel, card and tile is the one shared glass with its rule and brackets, no sheet spaces capitals by a number of its own, and every button is a pill or a circle | ADR: The glass look | StyleRulesTests |
| The Author page holds no address, phone number, email address, age, wedding or engagement, and none of the private names and dates kept off it, which are held as digests so the test does not publish them | ADR: The sidebar | AuthorPageTests |
| Every photograph the Author page serves is a file with no metadata, of the width its `srcset` claims, 1920 wide or with its reason written down, with an alt text, in the one frame; the headed blocks alternate by order; nothing else is in the folder | ADR: The sidebar | AuthorPageTests |
| The Admin tab's charts and gauges are drawn in the Mark VII marks, each 3:1 on white, and every figure the style page states for them is the figure the tokens give; the glass is 30 per cent white and the secondary grey holds 4.5 over it on the ribbons' teal and gold stops (`tokens.test.ts`); no table cell breaks a word (`coverage.spec`) | ADR: The tweaks pass | StyleRulesTests |
| A colour is written once in the token sheet, a token that repeats one is written as it, and a see-through tint is mixed from its token; every width a page asks about is a step on the one scale in `src/lib/breakpoints.ts`; every size, weight, corner, tracking and layer a stylesheet writes comes from the token sheet | ADR: The tweaks pass | StyleRulesTests |
| A shipped test-results file is one only a green gate writes, every suite in it with its counts equal to its rows and nothing failed or skipped, the two passes on Cosmos DB the only ones a gate may carry forward and then only from a named version, and the Admin tab's endpoint serves it or says there is none | ADR: The five-minute gate | TestResultsTests |
| A browser test that passes only on its retry is written 'r' and a shipped results file holding one is refused; the README's xUnit number, everywhere it is stated, is the gate's own xunit-sqlite count | ADR: The five-minute gate | TestResultsTests, PublicFaceTests |
| Every 400, 401 and 404 a status test asserts is a ProblemDetails with a title, a sentence in detail and no public cache header | ADR: The exception handler, and the endpoint that proves it | AccountEndpointStatusTests, BidEndpointStatusTests, LinkEndpointStatusTests, VehicleEndpointStatusTests |
| The clock-bound tests read the day and the windows from the answer, never from the test machine's clock | ADR: Three readers with no memory of the project | ActivityTests, ApiIntegrationTests |
| CI runs on every push to main that touches the site, on every pull request and by hand, and a push that touches only the sample skips it | ADR: The five-minute gate | CiTriggerTests |
| A line on a Cloud infrastructure card that is not read says so and never colours the card; the Cosmos DB card judges the site's own throttles, the live tests' copies counted apart | ADR: One frame for every resource | CardFrameTests, CloudThresholdTests |
| The shared Cosmos DB database never needs a twenty-sixth container: the definitions and the live tests' copies stay at its limit, and the nightly card keeps its minutes and runs in the plan container under kinds of its own | ADR: The Nightly Function on the Admin tab | CosmosDefinitionTests, NightlyCardTests |
| The listing and the filter values leave compressed for a caller that asks, and no other address is compressed | ADR: Cache headers | CompressionTests |
| The listing and the filter values may be kept by the edge for seconds, and what only a deploy changes (the documents, the diagrams, the share picture) for a day; never for a request that carries a cookie, and never the resume, About Steven, the version, the admin reads or the sign-in; the browser is still told no-cache; and every address the edge card scores is one the edge may keep | ADR: Cache headers | CacheHeaderTests, EdgeKeepsTests |
| The default listing reads its page off the day's schedule order, and that order is the full ending-soonest sort at every instant, ties included | ADR: The search index | ScheduleOrderTests |
| Program.cs is a table of contents under eighty lines that maps no route itself, every route is mapped from a file under `Endpoints/` or `Composition/`, and every class in those two folders is static | ADR: The composition root, split by job | CompositionRootTests |
| Every public class and record in the solution says what it is for in an XML summary, and every positional record names each parameter; only the generated migrations are excused, by name | ADR: The composition root, split by job | XmlSummaryTests |
| The bill on the Admin tab is read from Azure once an hour and never on a request, a resource path is cut to its name and type before anything reaches the wire, the donut names four resources and folds the rest into Others, and a reading that is absent is a sentence and never a zero | ADR: What Azure charges | CostTests |
| Every task in `.vscode/tasks.json` runs a script `package.json` declares or a project in the tree, every launch configuration runs a project that exists on the port its own `launchSettings.json` names, and the Working in VS Code page names every task and configuration | ADR: The editor is set up by the repository | WorkspaceTests |
| The gate on GitHub Actions runs every check the laptop's gate ran, writes every report the results writer reads, turns red when any check fails, runs The Shed's suites on Windows, and runs only on `gate/` branches with no permission to write | ADR: The gate runs where the memory is | GateWorkflowTests |
| The resume at /resume is the locked resume, byte for byte, inline under his name and the lock's date, and the door in its link is recorded as one allowed word, never the query | ADR: The resume has an address | ResumeAddressTests |
| The App Service plan is read a minute by the SQL site alone and kept thirty-one days, every window is drawn from the kept copy, and CPU, memory, the HTTP queue and sockets turn amber and red only when fifteen minutes in a row hold the line | ADR: The App Service plan, read every minute | PlanThresholdTests, PlanViewTests |
| Every resource under Cloud infrastructure is a definition drawn in the one frame, found in the resource group rather than typed, linked to the blade Azure returned, and turns amber and red at the lines its card names; a key the catalogue does not name is a 404 | ADR: One frame for every resource | CloudThresholdTests, CloudViewTests |
| The hundred thousand vehicles live in the store, each with the slot the hash gives it, and the slot is the hash's window on any day; the store filters, orders and pages and answers what the list in memory answers; a raised price is merged back where the full sort puts it; once the store answers the process holds none of the catalogue | ADR: The store does the work | StoreCatalogueTests, StockTests, CosmosDefinitionTests |
| Every collection a long-lived object keeps is named with what bounds it, every cache is bounded and expires, every outbound call shares one pool, and each .NET site runs under a heap limit of thirty per cent | ADR: The store does the work | MemoryRulesTests |
| A typed word of three characters or more matches the start of a word and a shorter one matches anywhere, on every store and every in-memory filter; on SQL a word page seeks the words table's word index and never reads every row's text; on Cosmos DB a page deep in the whole catalogue's order, the live segment by end, the upcoming by start or the ended by end is read from a kept place, the same rows as OFFSET for under 100 RU | ADR: The store does the work | VehicleSearchIndexTests, StoreCatalogueTests, CosmosKeysetTests |
| Memory that climbs while traffic is flat reads red on the Admin tab, and the week the catalogue moved into each process reads red on the readings the site kept | ADR: Memory follows traffic | MemoryTrafficTests |
| Every reader behind the plan card and the six resource cards runs against Azure's and Netlify's recorded answers through the one outbound pool: what it asks for, what it makes of the answer, and a refused metric, a refused query or an unseen site read as less and never as a failed card | ADR: One frame for every resource | CloudReadersTests |
| Rule 1 of the slices: every feature is a folder under the host's Features/ with a Module that registers and maps it, Program.cs is a loop over the modules under eighty lines that maps nothing, and a slice uses no other slice | ADR: The onion cut into vertical slices | SliceTests, CompositionRootTests |
| Rule 2 of the slices: no composition record that every registration takes whole; each registration is handed what it needs by name, and what runs at startup is one hosted service per concern | ADR: The onion cut into vertical slices | SliceTests |
| Rule 3 of the slices: the sites and the stores are a configured list, and the types that pick a request's site and store carry no store key and no host name | ADR: The onion cut into vertical slices | SliceTests, SiteHostsTests |
| A store's key is written once, in StoreKeys, and nowhere else in the host or the Application ring's source | ADR: Onion and SOLID, how this codebase holds them | SliceTests |
| Rule 4 of the slices: a backend and its attachment name no adapter in any member, however deep a generic argument goes, both stores answer one diagnostics port, and an endpoint outside the Admin tab and the health report never sees a backend | ADR: The onion cut into vertical slices | SliceTests, OnionTests |
| Rule 5 of the slices: a guest site stands behind IHostedSite, The Shed is its one implementation, and only ShedSite and ShedApplication use The Shed's assembly | ADR: The onion cut into vertical slices | SliceTests, OnionTests, ShedMountTests |
| Rule 7 of the slices: in every slice that has its handlers, each route asks exactly one Application handler, every handler is tested with fakes at its ports and no host, and every status code a route declares has an integration case | ADR: The onion cut into vertical slices | HandlerRuleTests, BidHandlerTests, BidEndpointStatusTests, VehicleHandlerTests, VehicleEndpointStatusTests, AccountHandlerTests, AccountEndpointStatusTests, PlanHandlerTests, PlanEndpointStatusTests, CloudCardHandlerTests, CloudViewTests, AdminEndpointsNightlyRunTests |
| Data has no behaviour (accessors, constructors and what the compiler writes for a record only), Data and Domain read neither the disk, the network nor the machine (a clock, a process, a type found by name), and an adapter neither uses the web beyond Identity nor decides on money | ADR: Onion and SOLID, how this codebase holds them | OnionTests |
| An endpoint class is any type that maps a route, whatever its name; the host queries neither SQLite nor any System.Data.Common connection itself; and a route delegate or a handler takes its settings, never IConfiguration | ADR: Onion and SOLID, how this codebase holds them | OnionTests |
| Rule 1 by folder: a file under Features/<Slice>/ declares that slice's namespace (one body from 1.0.4.70, tests/rules/NamespaceRule.cs, which the job hunt's suite runs over its three rings), and a host-root type one slice alone uses lives in that slice or is named with its record's reason | ADR: The onion cut into vertical slices | SliceTests, RingTests |
| The host root holds Program.cs alone; Features/ and Shared/ hold folders alone; every Shared concern is a row in the record's table and declares its own namespace; a shared type one slice alone uses is red with the slice named | ADR: The host root holds one file | HostLayoutTests, SliceTests, CompositionRootTests |
| Every request is a sealed `<Verb><Noun>Query` or `<Verb><Noun>Command` with exactly one handler implementing IQueryHandler or ICommandHandler and named for it; a query handler takes no store and no writer; nothing in the Application ring's root or slices is named *Service; the CQRS page's counts are the build's. From 1.0.4.43 the Admin tab's 24 reads are queries with one handler each over a port split by what it reads; fifteen routes stay exempt by name with a reason (the host's own files, health, pulse and error rings) | ADR: A request is a query or a command | CqrsTests, HandlerRuleTests, AdminQueryTests |
| One count of people who have visited, all time: one record a visitor under a keyed hash that does not rotate, never the address or the agent; who is a person is one Domain rule the Activity card and the count share; the collector writes a visitor once a day at most; the count is one query read by the Admin tab and the pulse | ADR: People who have visited, counted once | VisitorTests |
| Rule 2 for every hosted service: each is named with its one concern, has one public constructor and takes no service container | ADR: The onion cut into vertical slices | SliceTests |
| Rule 7 in every slice: a route asks one handler and takes nothing else this codebase defines but what is named beside it, or is exempt by name with its reason; a [Covers] counts only beside an assertion of its status, and a handler is tested with fakes only in a test that asserts | ADR: The onion cut into vertical slices | HandlerRuleTests |
| The front end's layers: src/lib imports nothing from React, a hook imports no component, a request is written in src/lib inside the working store, and the browser's ordering bands equal the Domain's | ADR: Three readers with no memory of the project | FileHeaderTests, WorkingLockTests |
| Rule 6 of the slices: outside the composition root, a list of types each with its reason, the host never reads the environment, a service container (GetService, GetRequiredService, ActivatorUtilities, a request's RequestServices) or the built app's configuration, and the Application ring never does at all; the platform's settings are read once into BuildInfo and HostIdentity | ADR: The onion cut into vertical slices | OnionTests |
| A session is refused on any store but the one that opened it, and a session ended on its own store opens neither | ADR: The onion cut into vertical slices | RefuseSessionTests, AuthTests |
| Every test class that bids, buys, resets, runs the room or runs the proof through the booted host is in the shared bid store's collection, so no two write the shared Cosmos DB test database at once | ADR: The onion cut into vertical slices | SharedStoreTests |
| The plan's socket limit follows the size the plan record names, and the free tier's storage allowance and the Basic read rate apply only while the record says free tier and Basic | ADR: The onion cut into vertical slices | PlanThresholdTests, CloudThresholdTests |
| A tagged link's open is counted by its six-character code and its path, a preview fetcher's or a scanner's is not, a pass-through key the configuration does not name is 404, and the card's wire holds codes, paths and counts and nothing that names a person | ADR: Tagged links, who opened what | LinkHandlerTests, LinkEndpointStatusTests |
| A Shed that will not start answers 503 and leaves the Yard up; a Shed request that fails past its handler is a 500 problem in TheYard's error record; The Shed's upload limit is The Shed's alone; The Shed is stopped by the last hosted service, after the drain | ADR: The onion cut into vertical slices | ShedMountTests |
| No name of this deployment, its resources, its edge, its public domain or its identity's ids, and no price, plan size or date other than a Resource Manager api-version, is written in the six projects' code; every string constant they compile is read back, and the allow list is empty | ADR: The onion cut into vertical slices | LiteralScanTests |
| Every card under Cloud infrastructure answers the same slots (title, link, tone, spec and its source, headline, dials, sparks, threshold chips, chart, rows, summary, read-at, refresh), every summary line and row names its source, one tone rule in Domain gives the card its tone, and the page reads every card as often as that rule assumes | ADR: The onion cut into vertical slices | CardFrameTests |
| The nightly lane's slots are one table in Domain that the Function compiles by a link and its clock asks, judged on time, late or missed from five-minute firing counts on both daylight-saving days; the nightly card's dials turn at their lines, every Azure line reads "not read: no role on" the configured group while the identity has none, the run log warns on one failed run and alarms on two, GitHub's runs are parsed by lane, the snapshot count is the Function's own, and a run the workflow posted is kept under GitHub's own id, read by the same rules GitHub's own list is read by, and preferred over a read of GitHub while every slot due since it was answered by a push | ADR: The Nightly Function on the Admin tab | NightlySlotsTests, NightlyCardTests, AdminEndpointsNightlyRunTests |
| The working lock shows only after a request a person started has run half a second, clears on the last one answered or failed, never for a background read, covers the page and takes its clicks while it is up, and never shows for a fast answer | ADR: One working lock for the whole site, after half a second | WorkingLockTests, working.test.ts, working.spec.ts |
| The nightly card asks the job hunt's Function whether the tracker merger is keeping up and keeps only the five numbers its health route answers, so no file name, row or log line from the share reaches the site; the row is good under five minutes waiting and a warning past ten | ADR: The tracker merger runs in Azure | NightlyCardTests |
| A usage document is kept only when every rule holds (lane, model, run, counts, slot, mode, a start inside seven days), the route refuses without the operator's key before it reads a body, a document posted twice is kept once, the Claude usage card draws through the catalogue with the slot join's two sentences and dry runs out of the averages, and no source file but the one client names the Anthropic API | ADR: Every Claude lane reports its usage | UsageRecordTests, UsageLogTests, AdminEndpointsUsageTests, ClaudeCardTests, OnionTests |
| Only Infrastructure opens a socket: no host type but Program and a slice's Module names an HttpClient, a handler or a client factory, and every client is built in OutboundHttp beside the one pool; from 1.0.4.70 one body, tests/rules/SocketRule.cs, that the job hunt's suite runs over its Domain and Application | ADR: The onion cut into vertical slices | OnionTests, MemoryRulesTests, RingTests |
| Every file in Domain, Application and both adapters sits under Features/<Slice>/ or Shared/<Concern>/ and declares its folder as its namespace; only the types Entity Framework maps stay at the relational adapter's root, each named | ADR: The onion cut into vertical slices | SliceTests |
| The nightly card's six process sections read the Function's run lines and request rows through one Kusto shape, the runs route answers a page of twenty for one of the six and a 404 problem for any other name, a closed run history asks for nothing, and every figure names its source | ADR-109, the addendum on the six processes | ProcessRunsTests, ProcessReadersTests, cloud.spec.ts, axe.spec.ts |
| The words a process section prints are the code's own: each timer's summary and the Function's paragraph are read from the repository at the version deployed, and the schedule table holds the Function's own cron strings | ADR-109, the addendum on the six processes | ProcessReadersTests, ProcessRunsTests |
| A blog picture is served from the site's own address under the hash of its bytes with a year's immutable cache, a name the rule does not make is never looked up, the host's memory answers a second read, and the container is private | ADR-113, the addendum on where the pictures live | BlogImageTests, BlogImageRouteTests |
| A picture is put on a post only with the operator's key, and is refused by the figure it breaks: over a megabyte, not a PNG, JPEG or WebP by its own bytes, over 2000 pixels wide, or without an alt text; its size is read from its own header | ADR-113, the addendum on putting a picture in | BlogUploadTests, BlogUploadRouteTests, blog.test.ts |
| The nightly card draws the Function's seven processes, the campaign among them with its switch, and its next run is the next 8 AM Central under either clock | ADR-109, the addendum of 1.0.4.60 | ProcessRunsTests, ProcessReadersTests, cloud.spec.ts |
| A promotion moves main only for a run whose gate and Shed jobs are green on the commit it promotes, one commit on main, its version up by one in the fourth number; and no deploy rolls a commit main has left | ADR-098, the addendum of 1.0.4.107 | PromoteScriptTests, PromoteWorkflowTests, MachineFreeWorkflowsTests |
| The campaign card reads counts only and never shows a name, a company, an address or a code, is red at two bounces in a day and at no send by 9 AM with leads ready, names the day its counts start, and its cap is the Function's own | ADR-118, Decision 6; ADR-102, the addendum of 1.0.4.63 | CampaignCardTests, CloudDrawingTests, cloud.spec.ts |
| Rule 1 across the rings: a type under Features/<Slice>/ in Domain, Application, either adapter or the host uses only its own slice and the Shared concerns in any ring, never another slice's type in any ring (the host's Admin sub-slices are the one Admin slice to the inner rings) | ADR-107, the addendum of 1.0.4.67 | SliceTests |
| A query handler is handed no port that can write: no constructor parameter among this codebase's types has a member, counting every inherited interface, whose name starts with Keep, Write, Save, Upsert, Delete, Add, Remove, Record, Put or Set; the name check stays beside it | ADR-115, the addendum of 1.0.4.68 | CqrsTests |
| Nothing in the job hunt Function's Domain or Application writes a mail search (no from:, subject:, in:inbox or newer_than: on a code line); a search is a MailSearch and only the Gmail adapter composes Gmail's syntax, every search held to the string it sent before | ADR-117, the addendum of 1.0.4.69 | RingTests, GmailQueryTests |
| Every CI and gate job installs .NET through .github/actions/dotnet, which tries the download twice a minute apart and then uses the runner image's own .NET 10 SDK, failing only when there is none | ADR-068 | DotnetInstallTests |
| The job hunt Function's Infrastructure reads the environment only in the four files named with their counts until stage B of ADR-117 hands each host its settings, and the rule bodies the two suites share live once under tests/rules/, linked into both test projects | ADR-117, ADR-075 the addendum of 1.0.4.70 | RingTests, OnionTests, SliceTests, FileShapeTests |
| The tracker's SQL copy is the CSV byte for byte: the file is cut at CRLFs outside quotes and put back identical, the mirror writes only the rows whose hash changed, and a rebuild from the copy is compared with the CSV, which stays the master | ADR-111, the addendum of 1.0.4.73 | TrackerSqlTests |
| The job hunt Function's slices: a type under a ring's Features/<Slice>/ uses only its own slice and the Shared concerns in any ring, shared code uses no slice, no file sits at a ring's root, and a use case that only reads is handed the reading half of a split port | ADR-107 and ADR-115, the addenda of 1.0.4.75 | SliceTests, CqrsTests |
| Each process is one deployable cut into modules that are its slices: a slice's files name no other slice in any ring, every host module registers through a Module.cs, and the site and the Function share rings and two named linked files, never a project | ADR-119 | ModularMonolithTests |
| Every incident under docs/incidents/ keeps the title, the status line and the eight sections in order, names a source in every Timeline row, names only versions the changelog shipped under What changed, says a state for every follow-up, and is listed in the index | ADR-120 | IncidentShapeTests |
| The Foreman's page keeps its five sections in order, every reading names its route, rung 0 names no cause without three agreeing sources, and every place to start names a record, a file and a test that exist | ADR-121 | ForemanTests |
| A card's live read that times out is the recorder's gap and the card answers from its keep; only the request's own cancellation stops the read | ADR-102 | CloudTimeoutTests |
| Every quality number the ratchet file holds has a row in the record that explains what it counts, both workflows run the scanner against that file, the gate's verdict reads its result, and the coverage floor stays at ninety and seventy-five | ADR: The numbers that may only fall | QualityRatchetTests |
| Every async method in production C# takes a CancellationToken but the one framework override whose signature this codebase does not own; the parameters a declaration takes are read across an attribute's brackets, a tuple's and the lines a wide signature wraps onto; and every timer the job hunt Function runs declares the token Azure hands it and passes it on | ADR: The numbers that may only fall | CancellationTests |
| Outside a composition root (a slice's Module, the wiring, Program.cs or the Clocks seam) a time is read from a TimeProvider handed in by constructor, and the thing it was handed to uses that clock rather than the real one | ADR: Onion and SOLID, how this codebase holds them | ClockSeamTests |
| How many things a class is handed is counted in parameters and not in commas, so a comma inside a generic type argument is not a separator and a parameter list that wraps is read to its closing bracket; the classes still handed six or more are named one by one and their number is the ratchet's limit; and every part split off one of them resolves from the real host | ADR: The numbers that may only fall | ConstructorWidthTests |

## How the table is read

`RuleTableTests` takes the last cell of every row, pulls every name ending in `Tests` out of it, and
requires each one to be a class in the test assembly with at least one test in it. Then it takes the
middle cell, pulls every record it names, and requires each one to be a record that exists by that exact
title. A row that cites a record which is later renamed fails here, and so does a row whose test is
deleted or renamed, which is the failure this table is for.

```live path=api/TheYard.Tests/RuleTableTests.cs region=table
```

## The order a change goes in

1. Read the record that governs the thing being changed. This table names it for the rules a machine
   holds; the Decision Records index in the sidebar holds the rest.
2. If the change contradicts a record, the record changes first, as an addendum that says when it
   stopped being true rather than an edit that makes it look like it was always this way.
3. Write the test with the change, in the same commit.
4. The five-minute gate runs green before anything rolls (ADR: The five-minute gate). Any build warning
   is red.

## What this does not claim

Most of what makes this codebase readable is not in the table, because no test finds it. Naming, the
direction a dependency points inside a file, a comment that has outlived its premise, a check that asks
an easier question than the one it was written for: those are held by Coding and Commenting Style and by
reading the work again before it ships (ADR: Reviewing my own work, and what that found). The table is
the floor, not the ceiling.

Nor does it claim the tests are the decisions. A rule is decided in a record, for a reason, and the test
is how the decision survives a busy month.

## Addendum, 2026-10-04: two rows changed

The catalogue keeper is gone (ADR: Kept awake, the addendum of 4 October), so the row that named `WarmthTests` now says what that test holds: a request on a cold store waits for the load without blocking a thread. `HouseVoiceTests` gained a second rule in 1.0.3.71, no production comment leaning on a version, a date or a review, and the table has its row.

## Where it sits

Beside the onion, in TheYard.Tests. RuleTableTests reads this table, and three of its rows are the OnionTests rules that hold the rings themselves.

## Files

- [`docs/decisions/ADR-075-the-rules-a-change-has-to-pass.md`](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-075-the-rules-a-change-has-to-pass.md): this record, which the table below it is read from.
- [`api/TheYard.Tests/RuleTableTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/RuleTableTests.cs): the test that keeps the table honest.
- [`api/TheYard.Tests/RecordShapeTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/RecordShapeTests.cs): the shape every record keeps.
- [`api/TheYard.Tests/RecordLinksTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/RecordLinksTests.cs): the links and citations, in both directions.
- [`api/TheYard.Tests/FileHeaderTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/FileHeaderTests.cs): the header every file in the app shell and the document library opens with.
- [`api/TheYard.Tests/FileShapeTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/FileShapeTests.cs): the 300-line rule for every production source file.
- [`CLAUDE.md`](https://github.com/SteveStout/TheYard/blob/main/CLAUDE.md): what an agent reads before it changes anything, which now points here.
- [`docs/app-architecture/STYLE.md`](https://github.com/SteveStout/TheYard/blob/main/docs/app-architecture/STYLE.md): the rules no test holds (served as Coding and Commenting Style under App Architecture).

## Addendum, 2026-10-03: four more rules

Four rows joined the table with the pass that put the rings under test and the fixes that followed it: three for `OnionTests`, which reads the compiled assemblies and fails the build when a dependency points outward, and one for `TechnologyVersionsTests`, which holds the versions record to the project files. `RecordShapeTests` also requires every record to say where it sits (ADR: Onion and SOLID, how this codebase holds them).

## Addendum, 2026-10-05: one more rule

CI runs on every push to main again (ADR: The five-minute gate, the addendum of 5 October), and `CiTriggerTests` holds the triggers, so the table has its row.

## Addendum, 2026-10-05 (1.0.3.77): compression, and the one address list it reads

The container compresses the catalogue's two reads and nothing else (ADR: Cache headers, the addendum on compression). `CompressionTests` holds both halves, so the table has its row.

## Addendum, 2026-10-05 (1.0.3.79): the edge's copy

The edge may keep the catalogue's two reads for a few seconds (ADR: Cache headers, the addendum on the edge's copy), and `CacheHeaderTests` holds who may be served from that copy and who may not, so the table has its row.

## Addendum, 2026-10-05 (1.0.3.80): the schedule order

The default listing stopped sorting the whole catalogue on every request (ADR: The search index, the addendum on the schedule order), and `ScheduleOrderTests` holds the faster path to the page the full sort gives, so the table has its row.

## Addendum, 2026-10-06 (1.0.3.84): the folders

The documents moved into one folder per sidebar section (ADR: The sidebar, the addendum on the folders), and `DocsFolderTests` holds where each one sits, so the table has its row.

## Addendum, 2026-10-06 (1.0.3.85): a fifth Style page

The Style section gained How the documents are styled (ADR: Live code samples, the addendum on the page that shows them), so the row `StyleSectionTests` holds says five pages, and the same test holds the page's two new live numbers.

## Addendum, 2026-10-06 (1.0.3.86): the folders have their own record

The row `DocsFolderTests` holds now cites ADR: One folder per sidebar section, the record Steve asked for so the folder rule is followed from here on, in place of the addendum to ADR: The sidebar that first described it.

## Addendum, 2026-10-09 (1.0.3.106): the documents and the plan

The plan moved to B2 on 8 October and five pages went on describing B1 (ADR: A rendering service beside the API, the addendum on the documents). `AppServiceTemplateTests` now reads the size from the template and holds every page that describes the machine to it, so the table has its row.

## Addendum, 2026-10-13 (1.0.3.123): the gate on GitHub Actions

The gate moved to a GitHub runner (ADR: The gate runs where the memory is). `GateWorkflowTests` holds the workflow to every check the laptop's gate ran, so the table has its row.

## Addendum, 2026-10-20 (1.0.3.188): the first rows of the slices

Four rows from the record that cuts the onion into vertical slices, for the rules that landed in 1.0.3.188: the composition record retired (rule 2), dependencies found by constructor and the platform's settings read once (rule 6), the session refused on the other store, and The Shed held at its edges. The record names seven rules; each gets its row in the commit that makes it hold.

## Addendum, 2026-10-20 (1.0.3.189): rule 1

One row for rule 1 of the slices: the host's features are folders with a module each, Program.cs loops over them, and `SliceTests` fails when a slice uses another or the shared part uses a slice. The row for the composition root's shape, decided in ADR: The composition root, split by job, still names `CompositionRootTests`; that test now holds the new shape.

## Addendum, 2026-10-20 (1.0.3.190): rules 3, 4 and 5

Three rows for the sites as a list, the stores behind one diagnostics port, and the guest site behind one interface.

## Addendum, 2026-10-20 (1.0.3.191): rule 7, starting with bidding

The row for rule 7 names the test that holds it, `HandlerRuleTests`, and the two the Bids slice brought: its handlers with fakes, and one integration case per status code. Each slice that gets its handlers joins the rule in its own commit.

## Addendum, 2026-10-25 (1.0.4.70): one rule, one body

Three rules were written twice, once in each test project: no socket outside Infrastructure, a file's namespace is its folder, and no source file past 300 lines. Two copies of a rule drift, and the second process is where a rule goes soft first. From this version each body lives once under `tests/rules/` (`SocketRule`, `NamespaceRule`, `LengthRule`), compiled into `api/TheYard.Tests` and `functions/jobhunt.Tests` by a link, the way JobHunt.Domain links NightlySlots.cs, and each suite hands it its own assemblies or folders. The job hunt's suite gains the 300-line rule it never had. `RingTests.Infrastructure_reads_the_environment_only_in_these_files` names the four Infrastructure files that still read the environment, with their counts, so a fifth fails. This table now resolves the job hunt's test classes too, read from `functions/jobhunt.Tests`.

```live path=tests/rules/NamespaceRule.cs region=namespace-rule
```

Proved red from the one body: the namespace rule broken on purpose in a scratch tree of each process failed both suites, named in the DONE note, and was reverted.


## Addendum, 2026-10-28 (1.0.4.113): two Cloud rows cover every card the catalogue names

Row 115 said "the plan card and the six resource cards" and row 139 "every card under Cloud infrastructure", which `CardFrameTests` held over nine of the eleven. Both stopped being the whole truth when the share, blob, claude and campaign cards were added (ADR: One frame for every resource, the addendum of 1.0.4.113). From this version neither row keeps a list of its own: `CloudReadersTests` builds the catalogue the host builds, all eleven cards over recorded answers, and `CardFrameTests` reads its routes from that same catalogue, so a card added is a card held in both rows without either row being edited again. Row 115 should be read as every reader behind the plan card and every resource card the catalogue names.

Rule 7 gains three cases. `/api/admin/azure`, `/api/admin/machines` and `/api/admin/follows` are the three `IResult` routes that declared no status, so rule 7 required nothing of them. The lane's question was whether they should declare one; the answer taken is yes for the 200 each of them answers, and the unknown-window case on machines and follows beside it, because a route that declares nothing is a route rule 7 cannot hold and these three are read by the Admin tab on every refresh. Each now carries `.Produces(200)` and a `[Covers]` case that asserts the status in its own body: `HourlyReadingsTests.The_azure_answer_carries_the_hourly_readings`, `MachinesTests.The_endpoint_answers_with_the_container_and_both_stores` with `The_endpoint_reads_a_window_it_keeps_and_an_unknown_one_as_the_hour` beside it, and `MemoryTrafficTests.The_endpoint_answers_the_window_its_points_and_a_verdict`, whose unknown window was already a case. No status beyond the 200 is declared: none of the three answers a 404 or a 400, and declaring one it does not answer would buy a test for an answer that cannot happen. Proved red first: with the metadata added and no case, `HandlerRuleTests.Every_status_a_ruled_route_declares_has_an_integration_case` read "no integration case covers: GET /api/admin/azure 200, GET /api/admin/machines 200, GET /api/admin/follows 200".


## Addendum, 2026-10-29 (1.0.4.114): rule 7 takes the Cloud tab's first write, and a fixture dated by the wrong clock

Every address under Cloud infrastructure has been a read until now, and the three that declared no status were given the 200 they answer at 1.0.4.113. `POST /api/admin/cloud/nightly/run` is the first write in that slice (ADR: The Nightly Function on the Admin tab, the addendum of 1.0.4.114), so it is the first one rule 7 has anything to say about beyond the 200.

**Four statuses declared, four cases.** The route declares 200, 400, 401 and 503 with `.Produces`, and each has a `[Covers]` case in `AdminEndpointsNightlyRunTests` that asserts that status through the booted host. The lane's step file expected three of them (200, 400, 401), and the fourth is written down here with its reason: the handler mirrors `KeepUsageHandler`, which reports the log's own reason when it cannot keep anything, and a route that answered 200 to a run it did not keep would be the one defect this whole feed cannot afford. The 503 case is not a conditional branch that may never run: it boots the host with `INightlyRunLog` replaced by a log that says it can keep nothing and would throw if written to, so the status is read and the log is proved untouched. The route also answers 400 for a body it cannot read at all, which is the same status and the same case.

**The 200 case has to ask the store first, and the first gate run proved it.** The laptop runs the suite with `--filter Store!=cosmos`, so the first push of this version was the first time these cases booted on Cosmos DB, and the kept-run case read "Expected: 200, Actual: 503". Nothing was wrong with the route: the gate's Cosmos test database has no plan container, which is where the run log lives, so the log reports itself unavailable and the route answers the 503 it should. `AdminEndpointsUsageTests` has carried the guard for this since the usage route shipped, and this case now carries the same one: it reads the log's availability before it posts, asserts the 503 and leaves when the log cannot keep anything, and holds the kept run on the boot that can. The rule, for the next route that writes: a case that asserts a document was kept asks the store whether it keeps documents here before it asserts anything, because the suite boots on two stores and only one of them has the container.

**Where the cases live.** 1.0.4.113's answer was that a status case does not need a file of its own, and it does not: the handler's own cases with fakes and no host are in `NightlyCardTests`, the class the nightly card already has, and the route's cases are in `AdminEndpointsNightlyRunTests` in the same file, which is how `ClaudeUsageTests.cs` already holds the usage route (its handler's cases and `AdminEndpointsUsageTests` share one file). They cannot be the same class: `HandlerRuleTests.Every_handler_is_tested_with_fakes_and_no_host` reads whether a test class boots a host, and `NightlyCardTests` must stay host-free to hold the handler.

**Proved red first, each quoted in the DONE note.** The eight cases that could compile against 1.0.4.113 were run alone on it: the route's four read 405 against the page fallback because no such address existed, the reader's two read "2 runs read" where "3 runs kept" was asserted, and the workflow's read "nightly.yml has no nightly-run-report region: the run does not post itself, so the card's runs can only come from GitHub". One of the eight passed on the prior tree, and that is written down rather than explained away: the case that a slot due with nothing pushed sends the hour back to GitHub holds the fallback that already worked, so it is a guard against losing it, not a defect it found. Rule 7's own obligation was then read red with the route and the handler in place and no host-free test for it: "no host-free test constructs: TheYard.Application.Features.Admin.Cloud.KeepNightlyRunHandler".

**A fixture dated by the wrong clock, found on main and fixed here.** `CloudReadersTests.Each_card_left_out_of_the_recorded_answers_reads_its_own_source_and_answers_in_the_frame` gained its campaign case at 1.0.4.113, and its recorded reader dated its two days by `DateTime.UtcNow` while `CampaignSource.Today` and the campaign card both read the Central day off the moment (`NightlySlots.Local`). The two zones are on the same date for nineteen hours a day, so the case read the wrong day's row and failed from 05:00 to 24:00 UTC and passed from 00:00 to 05:00. 1.0.4.113's gate ran at 03:52 UTC, inside the window, and no run of the suite touched main again before this version, so the red was never seen. The fixture now dates its days Central, with the reason written beside it. The lesson, for any fixture after this one: a recorded answer that carries a date carries the date the code under it reads, not the one the test writer happened to think in, and `DateTime.UtcNow` in a fixture for a card that thinks in Central is a test that passes for five hours a day.


## Addendum, 2026-10-29 (1.0.4.115): the numbers beside the rules

Every row above asks a yes-or-no question, and in the two weeks to 29 October every one of them held while six things nobody was counting roughly doubled (ADR: The numbers that may only fall). Fourteen counts now sit in `.github/quality-ratchet.json`, `.github/quality.py` reads them off the checkout, CI's api job and the ship gate hold each one to its limit, and a limit moves only in the commit that earns it. The table has its row. This record keeps the rules; the new one keeps the numbers, and the difference between them is the difference between a thing that is broken and a thing that is getting worse.
## Addendum, 2026-10-29: one row for what is left of the cancellation count

The rung that threads the request's cancellation through every I/O path is finished, and the count
it was measured by is 1: an override of a framework type that can never take one. A number at its
floor is worth a rule, because the next change that adds an async method with no token would make
the count worse and say only that, whereas the row above names what is expected and where the one
exception is. The same row holds the two things that made the count larger than the code deserved:
the scanner reading a declaration's parameters up to the first closing bracket, and a Function timer
that declares the token and never passes it on (ADR: The numbers that may only fall, the addendum on
reading a whole signature).
## Addendum, 2026-10-29: one row for the clock that is handed in

`ADR: Onion and SOLID, how this codebase holds them` defended a static clock until the addendum
of this date, so until this version there was no rule to put in this table. There is now, and it
is worth a row for the reason the cancellation row above is: the ratchet's count falls when a
parameter appears, and the rule is that the parameter is used. The test moves a clock it owns
rather than reading a signature, because a class that takes a `TimeProvider` and reads
`DateTimeOffset.UtcNow` anyway satisfies a count and nothing else.
