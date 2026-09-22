# Start here

This page is for a developer who has just been handed this repository and has an hour. It says what to run, what to read, where a change goes and what the gate will ask of it. Everything here is true of the build you are reading it in, because it is served from inside the running app.

## Run it

```
npm install
npm start          # the .NET API and the React app together; opens the browser
```

That is the whole setup. There is no database to install: with no connection string the API writes to a SQLite file in your temp folder and deletes it on shutdown, and the catalogue is read from JSON files in `data/`, so a fresh clone serves 100,000 vehicles on the first run.

Three more commands, and they are the same three the ship's gate runs:

```
npm test           # Vitest, the presentation logic
npm run test:api   # xUnit, the domain, the application and the API
npx playwright test  # the browser suite, which starts both servers itself
```

## The shape, in one minute

The back end is an onion and its dependencies point inward only. `TheYard.Data` holds records with no behaviour; `TheYard.Domain` holds the rules; `TheYard.Application` holds the use cases behind three ports; `TheYard.Infrastructure` and `TheYard.Infrastructure.Cosmos` implement those ports over Azure SQL Database, SQLite and Azure Cosmos DB; `TheYard.Api` is the host and the composition root. One process serves both stores and picks one per request, which is why the site has two public addresses and one codebase.

The front end keeps the same discipline: `components` use `hooks` use `lib`, and **`src/lib` imports nothing from React**, which is what lets the arithmetic be tested without a browser.

## The five rules that will bite you first

1. **Derive, do not store.** Auction windows come from the item id by hash, status from the window and the clock. Nothing schedule-related is persisted.
2. **The server owns every derived fact, and the clock.** Never re-implement auction maths in TypeScript. An earlier version did it on both sides and drifted on a daylight-saving change.
3. **The wire is snake_case** and matches the dataset exactly. No mapping layer.
4. **Empty is valid, null is the error.** A collection is never null.
5. **Money is whole dollars as `int`; every instant is milliseconds since the epoch as `long`, UTC.**

## Where a change goes

- A rule about bidding goes in `TheYard.Domain/BidRules.cs` and nowhere else.
- An endpoint is a route, a call into `TheYard.Application`, and a result. No business logic in `TheYard.Api`.
- Presentation arithmetic goes in `src/lib` with a test beside it; a component reads it.
- A colour, a space or a font size goes in `src/styles/tokens.css`. A raw hex in a component fails the style test.
- A decision that someone could reasonably disagree with goes in `docs/ADR-*.md`, as a record with what was considered and what it cost. The Decision Records section of this site is that folder.

## What the gate asks

Every version runs one gate before anything rolls: Prettier, oxlint, TypeScript, the SQL project, Vitest, xUnit on SQLite, the live Cosmos DB tests, xUnit on Cosmos DB, and the browser suite on both stores. A build warning is red. A number quoted in a document is read back against the build, so a stale count in prose fails the suite rather than surviving in the page. The evidence strip on the landing page is those counts, read from the file the gate wrote.

## Reading order for the rest of the hour

1. **Project README**, for what the thing is and how it is put together.
2. **ADR: The rules a change has to pass**, which lists each rule beside the test that enforces it.
3. **App Architecture** and **Projects**, for the onion and the nine projects.
4. **Built with AI**, for how this repository was actually written, and what was checked by hand.
5. Any **decision record** whose title sounds like the change you are about to make.

If a document and the code disagree, the code is right and the document is a bug. Fix it in the same commit.
