# Coding and Commenting Style

The rules this codebase is written to. The architecture page says which
way the dependencies point; this one says what the code looks like once it
is there. Both exist because the README promised them at the start of the
build, and because a rule that lives only in someone's head cannot be
reviewed against.

The mechanical half of this page is enforced by
[`.editorconfig`](https://github.com/SteveStout/TheYard/blob/main/.editorconfig)
and the compiler settings, not by review: indentation, line endings, using
order, `var` usage, unused locals and parameters. Review is for the half a
tool cannot check.

## Naming

- C#: `PascalCase` for types, methods and properties, `camelCase` for
  locals and parameters, `_camelCase` for private fields. Interfaces that
  are ports read as the thing they provide (`IVehicleSource`), not as the
  implementation.
- TypeScript: `camelCase` for values and functions, `PascalCase` for types
  and components, `SCREAMING_SNAKE` for module constants
  (`FILTER_DEBOUNCE_MS`, `EMPTY_FILTERS`).
- The wire is snake_case end to end because the dataset is
  (`body_style`, `min_next_bid`). Nothing is renamed in transit.
- A test name is a sentence: `Increments_are_tiered`,
  `Live_auction_accepts_a_bid_at_the_minimum_and_rejects_below_it`. A
  failing run should read like a report.
- Files are named for what they hold, one main type each: `BidRules.cs`,
  `VehicleCard.tsx`, `VehicleCard.module.css` beside it.

## Layering

- Dependencies point inward. `TheBlock.Data` references nothing;
  `TheBlock.Domain` may reference Data; Application talks to
  Infrastructure only through ports. If a file needs a `using` that points
  outward, the code is in the wrong project.
- Domain code is pure: no `DateTime.Now`, no filesystem, no HTTP. Time
  arrives as an `AuctionClock` the caller built, which is why the tests
  can anchor it.
- The host binds and delegates. A rule in Program.cs is a bug in layering.
- The browser holds no business rules. If a calculation decides money or
  eligibility, it belongs in Domain and travels on the wire.
- `src/lib` is plain TypeScript with no React import; `src/hooks` is React
  state more than one component needs; `src/components` render.

## Comments

The rule is **why and how, never what**. The code already says what it
does; a comment earns its line by saying why this way, what breaks
otherwise, or what a reader could not know from the syntax.

```csharp
// Materialize the inventory now so a bad dataset fails the process at
// startup, visibly, and not as a 500 on the first request.
app.Services.GetRequiredService<InventoryService>().GetAll();
```

That comment is worth keeping: the line is one call, and the reason is a
deployment decision. A comment reading `// get all vehicles` would not be.

Four more habits:

- A public C# member gets a `<summary>` when its name cannot carry the
  whole contract. `IVehicleSource.Load()` needs none; `LiveSamples.Expand`
  needs several lines.
- A file that implements a decision names the record: `(ADR-015)`,
  `(ADR: The palette)`. That is how a reader gets from a line to the
  reasoning behind it.
- A comment that explains a workaround says what would happen without it,
  because that is the part that decides whether it can be removed later.
- Code shown in a served record is documented by that record, and carries
  teaching comments for a reader meeting the pattern for the first time
  (ADR: Docs and testing). Code no record shows keeps its own comments.

## Tests

- The suite is chosen by what the code depends on: pure rules get xunit
  unit tests, anything needing the host gets an integration test through
  `WebApplicationFactory`, anything needing a browser gets Playwright.
- A test builds what it needs with the builders in `TestData`, overriding
  only the fields it cares about.
- Time is anchored, never `Now`. Shared state is reset in `beforeEach` or
  isolated in its own fixture class.
- Browser tests locate by role and accessible name, which keeps the suite
  honest about accessibility.
- ADR: The tests, explained walks all three suites for a newcomer.

## Formatting

- Four spaces in C#, two in TypeScript, CSS and JSON. UTF-8 everywhere;
  the files that carry a byte-order mark or CRLF keep them, because
  rewriting a file's encoding is a diff nobody asked for.
- Lines wrap around 100 characters in C# and 100 in TypeScript. Prose in
  the documents wraps at about 76 so a diff of a paragraph is readable.
- One statement per line; no single-line `if` bodies without braces.
- Prose in this repository, including these documents and every commit
  message, uses no em dashes. That is the house voice, not a style
  preference, and the ship gate counts them.

## Files

- [`.editorconfig`](https://github.com/SteveStout/TheYard/blob/main/.editorconfig): the mechanical rules, applied by the editor and the compiler.
- [`docs/ARCHITECTURE.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ARCHITECTURE.md): the layers these rules protect.
- [`api/TheBlock.Api/TheBlock.Api.csproj`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Api/TheBlock.Api.csproj): nullable reference types and implicit usings on.
- [`tsconfig.app.json`](https://github.com/SteveStout/TheYard/blob/main/tsconfig.app.json): `strict`, `noUnusedLocals`, `noUnusedParameters`, explained option by option in ADR: The React configuration, explained.
- [`docs/ADR-006-docs-and-testing.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-006-docs-and-testing.md): the documenting rule the comment section points at.
- [`docs/ADR-017-staff-review.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-017-staff-review.md): the review pass these rules are reviewed against.
