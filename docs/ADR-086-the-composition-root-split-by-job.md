# ADR: The composition root, split by job

Status: accepted, 2026-09-28, shipped as 1.0.3.39. Steve approved the target shape on 28 September: Program.cs a table of contents, one file per concern, one static class per feature.

## Context

ADR: Program.cs, explained kept the host in one file on purpose, and named what would change its mind: the composition and the routes no longer fitting in a reader's head together. Measured at 1.0.3.36, before anything moved:

| | Before |
| --- | --- |
| Program.cs | 2,667 lines: 934 comment, 151 blank, 1,582 code |
| Endpoints mapped in it | 48 |
| Regions in it | 66 |
| Live samples in the documents that point at it | 38, across 16 documents |

A reader looking for how a bid is handled scrolled past the stores, the rings, the accounts and the pipeline to find it, and a reader looking for the middleware order found it spread across four places, because an `app.Use` line runs in registration order wherever it sits among the routes.

## Decision

Program.cs lists what the app is made of and in what order, and each line leads to the file that shows how:

- `Composition/`: one static class per registration step (the stores, the observability rings, sessions and accounts, activity, email, the wire's shape, telemetry, the Admin tab's readers), the startup work after `Build()`, the whole pipeline in one method with the reason beside each piece, and the single-page fallback.
- `Endpoints/`: one static class per feature (vehicles, bids, documents, health, errors, the Admin tab, accounts, the OpenAPI reference), each with one `Map...Endpoints` method and private handlers named for what they answer.
- The records a feature binds or answers with sit beside it (`BidRequest` in the bids file, `ClientErrorReport` in the errors file).

What the approved sketch guessed and the real code answered differently, where the real code won:

- The stores are brought up before anything is registered, and that is awaited, so the step is `AddTheYardStoresAsync` and Program.cs awaits it.
- The steps hand one another what they made (the signing key, the stores, the kept log). `YardComposition` carries it, filled once at startup. It holds state, so it sits at the project's root beside `HostStart`, `ErrorRings` and `KeepWarmState`; every class in `Composition/` and `Endpoints/` is static.
- Moved code kept its namespace, `TheYard.Api`, because thirty-two test files import it and no existing test was to change.
- No `Options/` or `Background/` folder yet: no setting was bound through the options pattern to move, and the background services already live one per file at the project's root. Only code that left Program.cs went into the new folders; the root files are named in the review as a later pass.

## The numbers

| | Before (1.0.3.36) | After (1.0.3.39) |
| --- | --- | --- |
| Program.cs | 2,667 lines | 54 lines |
| Endpoints mapped in Program.cs | 48 | 0 |
| Files that map routes | 1 | 9, under `Endpoints/` and `Composition/` |
| The OpenAPI document | | the same operations and schemas; the paths and the tag list in a new order, because they follow the order routes are mapped in (Admin now first appears before Accounts) |

Every region moved with its code under its own name, and every live sample that pointed at Program.cs points at the file the code is in now.

## What holds it

- `CompositionRootTests`: Program.cs under eighty lines, no route mapped from Program.cs itself, every route mapped from a file under `Endpoints/` or `Composition/`, and every class in those folders static.
- `XmlSummaryTests`: every public class and record in the solution carries a summary saying what it is for, and every positional record names each parameter; the generated migrations are excused by file name, and the test fails if an excused file disappears.
- `ApiDocumentTests`, unchanged: the document the API publishes about itself. The ship's check also read the document from the running API before the move and after each of the nine commits and compared them, with the paths and the tag list taken as sets.
- Every test that existed before this change passes without an edit.

```live path=api/TheYard.Tests/CompositionRootTests.cs region=composition-root-rules
```

## Files

- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the table of contents.
- [`api/TheYard.Api/Composition`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Api/Composition): the registration steps, the startup, the pipeline and the fallback.
- [`api/TheYard.Api/Endpoints`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Api/Endpoints): one static class per feature.
- [`api/TheYard.Api/YardComposition.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/YardComposition.cs): what the steps hand one another.
- [`api/TheYard.Tests/CompositionRootTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/CompositionRootTests.cs) and [`api/TheYard.Tests/XmlSummaryTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/XmlSummaryTests.cs): the two rules above.
