# ADR: The tests, explained for a new developer

Status: accepted, 2026-09-02, shipped as 1.0.0.27. The third record written
at Steve's request for a developer new to the stack: the first two walk
Program.cs and the React configuration; this one walks the three test
suites, how each is built, and why there are three.

## Context

The repository carries 139 xunit tests under `api/TheBlock.Tests`, 36
Vitest tests beside the code they test under `src/`, and 25 Playwright
tests under `tests/e2e`. Every push runs all three in CI, and nothing
ships to the live site without them (ADR: Docs and testing). A newcomer
sees three runners, three folders and three vocabularies, and the
question is which one to reach for when something changes. The short
answer: the suite is chosen by what the code depends on. Pure rules get
unit tests. Anything that needs the host gets an integration test through
the real host. Anything that needs a browser gets a browser.

## The walk

### xunit: a Fact is one case, a Theory is a table

A `[Fact]` is a test with no parameters. A `[Theory]` takes rows of
`[InlineData]` and runs once per row, which is how a rule with tiers is
covered without six copies of the same test. Assertions are the static
`Assert` class; the test's name is a sentence with underscores, so a
failing run reads like a report. The domain tests need no host and no
file: `BidRules` is a static class of pure functions, so the test hands it
values and checks the answer.

```live path=api/TheBlock.Tests/BidRulesTests.cs region=tiers
```

### Builders instead of fixtures files

Every test that needs a vehicle asks `TestData.Vehicle(...)` for one and
overrides only the fields it cares about. A test reads as its intent
(`currentBid: null`, `bodyStyle: "van"`) instead of a forty-line object,
and a new required field on `Vehicle` is added in one place. The clock
helper does the same for time: an `AuctionClock` anchored to a chosen
midnight, so a test never depends on when it runs.

```live path=api/TheBlock.Tests/TestData.cs region=builders
```

### Fakes at the ports

`InventoryService` takes an `IVehicleSource` and an `IPhotoManifestSource`.
In production those are the JSON file adapters; in a test they are two
tiny classes declared in the test file with the `file` modifier, which
keeps them invisible to every other file. That is the onion architecture
paying for itself: the service is tested with no filesystem, and the
`LoadCalls` counter proves the dataset is read once.

```live path=api/TheBlock.Tests/InventoryServiceTests.cs region=fakes
```

### Integration tests boot the real host in memory

`WebApplicationFactory<Program>` starts Program.cs inside the test process
with an in-memory server, so `_client.GetAsync("/api/vehicles")` runs the
real routing, binding, services and serializer with no port and no
process. `IClassFixture` shares one host across a class, which is why the
bid lifecycle has a class of its own: its bids would otherwise leak into
the read-only tests. The `public partial class Program;` line at the end
of Program.cs exists for this factory (ADR: Program.cs, explained).

```live path=api/TheBlock.Tests/BidFlowIntegrationTests.cs region=lifecycle
```

Two habits in that test are worth copying. The anchor is captured once and
sent with every request, so the whole test sees one clock. And the target
vehicle is chosen by sorting live auctions by most bids: its window ends
hours or days out, so it cannot flip to ended halfway through the test.

### Vitest: the browser's logic without a browser

`src/lib` is plain TypeScript, so its tests run in Node in milliseconds.
The interesting ones are the cache tests: `vi.stubGlobal('fetch', ...)`
replaces the network with a counter, and fake timers let a five-minute
TTL expire in a line. Vitest is configured in `vite.config.ts` (ADR: The
React configuration, explained), and the include pattern keeps it to
`src/**/*.test.ts`.

```live path=src/lib/data.test.ts region=cache-tests
```

### Playwright: the real stack, one test at a time

The end-to-end suite drives the real Chrome against both servers, which
the config starts itself, so `npm run test:e2e` needs nothing running
first. Bids mutate shared API state, so the suite runs serially and resets
the bids before every test. Locators are by role and accessible name
(`getByRole('button', { name: 'Load more vehicles' })`), which tests what
a visitor sees and keeps the suite honest about accessibility. The URL is
an assertion surface of its own, because the address bar is the app's
state (ADR: The React configuration, explained).

```live path=tests/e2e/smoke.spec.ts region=get-navigation
```

```live path=playwright.config.ts region=web-servers
```

### The same three, in CI and before every ship

Three CI jobs, one per suite, on every push; the end-to-end job installs
Chrome first. The frontend job also runs `tsc -b` and the production
build, so a type error fails the run before any test does.

```live path=.github/workflows/ci.yml region=ci-jobs
```

## What to change when

- **A new business rule:** a domain test first, pure, with a Theory if the
  rule has tiers or boundaries; then the rule.
- **A new endpoint:** an integration test through the factory that asserts
  the status code and the wire shape, including the 400 path.
- **A new piece of browser logic:** put the logic in `src/lib` and unit
  test it; components render and are covered by the browser suite.
- **A new page behavior:** one Playwright test, by role, asserting the URL
  where the URL is the state; reset shared state in `beforeEach`.
- **A flaky test:** the cause is almost always time or shared state; anchor
  the clock, or give the test its own fixture class.

## Files

- [`api/TheBlock.Tests/TheBlock.Tests.csproj`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/TheBlock.Tests.csproj): xunit, the test SDK, `Microsoft.AspNetCore.Mvc.Testing` for the factory, and a global `using Xunit`.
- [`api/TheBlock.Tests/TestData.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/TestData.cs): the builders.
- [`api/TheBlock.Tests/BidRulesTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/BidRulesTests.cs), [`VehicleFilterTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/VehicleFilterTests.cs), [`AuctionScheduleTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/AuctionScheduleTests.cs), [`PhotoGalleryTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/PhotoGalleryTests.cs): the domain, pure.
- [`api/TheBlock.Tests/InventoryServiceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/InventoryServiceTests.cs) and [`BidServiceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/BidServiceTests.cs): the application layer with fakes at the ports.
- [`api/TheBlock.Tests/JsonFileSourceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/JsonFileSourceTests.cs) and [`SyntheticVehicleSourceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/SyntheticVehicleSourceTests.cs): the infrastructure, against the real files.
- [`api/TheBlock.Tests/ApiIntegrationTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/ApiIntegrationTests.cs), [`BidFlowIntegrationTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/BidFlowIntegrationTests.cs), [`AdminEndpointTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/AdminEndpointTests.cs), [`ProblemDetailsTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/ProblemDetailsTests.cs), [`CacheHeaderTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/CacheHeaderTests.cs), [`DocsCatalogTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/DocsCatalogTests.cs), [`LiveSamplesTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/LiveSamplesTests.cs), [`DiagramPageTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/DiagramPageTests.cs), [`ChangelogTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/ChangelogTests.cs): through the real host.
- [`src/lib/data.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/data.test.ts), [`inventory.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/inventory.test.ts), [`auction.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/auction.test.ts), [`format.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/format.test.ts), [`src/styles/tokens.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.test.ts): Vitest.
- [`tests/e2e`](https://github.com/SteveStout/TheYard/tree/main/tests/e2e) and [`playwright.config.ts`](https://github.com/SteveStout/TheYard/blob/main/playwright.config.ts): the browser suite and the two servers it starts.
- [`.github/workflows/ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml): the three jobs.
