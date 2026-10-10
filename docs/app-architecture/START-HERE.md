# Start here

This page is for a developer who has just been handed this repository and has an hour. It says what to run, what to read, where a change goes and what the gate will ask of it. Everything here is true of the build you are reading it in, because it is served from inside the running app.

## In plain words

This page gets a developer new to this code running in two commands and reading in the right order. It also says where each kind of change goes and what the tests that run before every release (the gate) ask of it.

Why it matters: a developer can make a first change within the hour without asking anyone where it goes, and the organization can hand this repository to a new hire with no walkthrough.

## Run it

```
npm install
npm start          # the .NET API and the React app together; opens the browser
```

That is the whole setup. There is no database to install: with no connection string the API writes to a SQLite file in your temp folder and deletes it on shutdown, and the catalogue is read from JSON files in `data/`, so a fresh clone serves 100,000 vehicles on the first run.

Three more commands, one for each suite the ship's gate runs (the gate adds a store filter and runs xUnit once per store):

```
npm test           # Vitest, the presentation logic
npm run test:api   # xUnit on SQLite; the seven live Cosmos DB tests run on the build machine
npx playwright test  # the browser suite, which starts both servers itself
```

## The shape, in one minute

The back end is an onion and its dependencies point inward only. `TheYard.Data` holds records with no behaviour; `TheYard.Domain` holds the rules; `TheYard.Application` holds the use cases and the ports they read through; `TheYard.Infrastructure` and `TheYard.Infrastructure.Cosmos` implement those ports over Azure SQL Database, SQLite and Azure Cosmos DB; `TheYard.Api` is the host, with its handlers in `Endpoints/` and the composition root in `Composition/`. `OnionTests` fails the gate when a dependency points outward ([ADR-088, Onion and SOLID](https://theyard.stevenstout.biz/?doc=adr-onion-and-solid)). One process serves both stores and picks one per request, which is why the site has two public addresses and one codebase.

The front end keeps the same discipline: `components` use `hooks` use `lib`, and **`src/lib` imports nothing from React**, which is what lets the arithmetic be tested without a browser.

Beside the two, `render/` is the rendering service: a small Node program that draws any page a visitor can link to, with its data in the HTML, by reading the API like any other client. It is the front end's own code drawn somewhere else, with about 300 lines of its own, and the API does not know it exists ([ADR-096, A rendering service beside the API](https://theyard.stevenstout.biz/?doc=adr-a-rendering-service-beside-the-api)). `npm start` runs without it; the browser draws every page then, as it did before the service.

## The five rules that will bite you first

1. **Derive, do not store.** Auction windows come from the item id by hash, status from the window and the clock. Nothing schedule-related is persisted.
2. **The server owns every derived fact, and the clock.** Never re-implement auction maths in TypeScript. An earlier version did it on both sides and drifted on a daylight-saving change.
3. **The wire is snake_case** and matches the dataset exactly. No mapping layer.
4. **Empty is valid, null is the error.** A collection is never null.
5. **Money is whole dollars as `int`; every instant is milliseconds since the epoch as `long`, UTC.**

## Where a change goes

- A rule about bidding goes in `TheYard.Domain/BidRules.cs` and nowhere else.
- An endpoint is a route in `api/TheYard.Api/Endpoints/`, a call into `TheYard.Application` (`Auction` for anything about the auction), and a result. No business logic in `TheYard.Api`.
- Presentation arithmetic goes in `src/lib` with a test beside it; a component reads it.
- Anything a page needs read before it is drawn on the server goes in `render/loaders.ts`, and nothing the server draws may differ from the browser's first draw; `src/app/drawServer.test.ts` draws every view with no browser, and `tests/e2e/drawn.spec.ts` takes each one over in Chrome and fails on a hydration error.
- A colour goes in `src/styles/colors.css`, a space or a size in `sizes.css`, a font size or weight in `typography.css`, a shadow, blur or opacity in `effects.css`. A raw hex in a component fails the style test.
- A decision that someone could reasonably disagree with goes in `docs/decisions/ADR-*.md`, as a record with what was considered and what it cost. The Decision Records section of this site is that folder.

The loaders, read from this build, because they are the one file on that list a new developer has not met before: one read per view, all at once, under one deadline, and a read that misses it is left to the browser.

```live path=render/loaders.ts region=loaders
```

## What the gate asks

Every version runs one gate before anything rolls: Prettier, oxlint, TypeScript, the SQL project, Vitest, xUnit on SQLite, the live Cosmos DB tests, xUnit on Cosmos DB, the browser suite on both stores, and The Shed's own xUnit and Node tests. A build warning is red. A number quoted in a document is read back against the build, so a stale count in prose fails the suite rather than surviving in the page. The evidence strip on the landing page is those counts, read from the file the gate wrote.

## Reading order for the rest of the hour

1. **Project README**, for what the thing is and how it is put together.
2. **ADR: The rules a change has to pass**, which lists each rule beside the test that enforces it.
3. **App Architecture** and **Projects**, for the onion and the ten projects.
4. **Built with AI**, for how this repository was actually written, and what was checked by hand.
5. Any **decision record** whose title sounds like the change you are about to make.

If a document and the code disagree, the code is right and the document is a bug. Fix it in the same commit.
