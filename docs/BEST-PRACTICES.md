# Best Practices

The record of engineering practices this project holds itself to, written for
a hiring manager or anyone learning from the code. Nothing here is just
claimed: every practice is visible in the running site or the repository.
Decision records live under this menu as it grows.

## Show it, don't say it

Everything this project asserts about itself is served by the project. The
docs, the decision records, the infrastructure code, and the resume all come
from menus in the running app. The version number in the page footer follows
the same rule: the running container reports which build it is, and the
footer displays exactly that.

## The practices, as they stand

- **Version and build provenance.** The footer shows the version and the
  exact commit the running container was built from, baked in at image build
  time so the number cannot drift from the truth. Recorded in
  ADR: Version in the footer, below this entry in the menu.
- **Tests gate every ship.** Three suites run before any image is built:
  the API tests, the frontend unit tests, and the Playwright end-to-end
  checks. A red suite stops the pipeline, no exceptions.
- **The architecture is written down, not remembered.** One page for the
  layers and the rules that keep them, one for naming, layering and
  commenting, and an `.editorconfig` doing the mechanical half. Both are
  served under App Architecture in the sidebar, beside the records that
  walk the code.
- **Decisions get written down.** Twenty-nine ADRs record why the architecture is
  what it is, including reversed decisions, the production design that is
  deliberately left undeployed, and the documentation and testing rules
  themselves.
- **Infrastructure as code.** The production design lives in Bicep, served
  under the Hosting menu, deployable by flipping parameters.
- **Containers run hardened.** Multi-stage build, a non-root user, a real
  HTTP healthcheck, and no SDK in the runtime image.
- **Every failure has one shape.** Rejected queries, rejected bids and
  unhandled exceptions all answer RFC 9457 ProblemDetails with the message
  in `detail` and a trace identifier; every request is logged as
  structured JSON; a React error boundary turns a render crash into a page
  with a way out, and reports it to the same list the Admin tab reads.
  Recorded in ADR: Error handling.
- **The style rules are checked by machines, not by memory.** Four checks fail
  the build: `dotnet format` on the C#, Prettier on the TypeScript and CSS,
  oxlint on what a formatter cannot see, and the typecheck that was already
  there. Each one was run against deliberately bad input to prove it fails.
  Recorded in ADR: Style, enforced.
- **One implementation of every rule, including the simulated one.** The
  competing bidders place their bids through the same `BidRules` the visitor's
  bids go through, and whether the visitor is still ahead is answered by the
  server rather than by two numbers compared in a browser. A second, quieter
  implementation of the auction is exactly what the layering here exists to
  prevent. Recorded in ADR: Competing bidders.
- **The keyboard path is walkable end to end.** A skip link past the rail,
  focus that follows the view instead of falling to the body, a live region
  that names what you arrived at, and a palette measured against WCAG AA
  before it was chosen. Recorded in ADR: Keyboard and screen reader.
- **Work that does not depend on the request happens before it.** Each
  vehicle's searchable text is built once when the dataset loads, and a query's
  tokens once when the filter compiles, so a full-text scan of the hundred
  thousand rows stopped rebuilding both per row. The suite carries the
  measurement rather than the claim. Recorded in ADR: The search index.
- **Telemetry outlives the container.** Every request, dependency and
  exception goes to Application Insights, browser errors included, and the
  Admin tab reads the last hour back with the container's own identity. The
  ingestion key is never in the repository: the deploy reads it from Azure at
  roll time. Recorded in ADR: Telemetry.
- **The system reports on itself.** The Admin tab in the sidebar shows live
  health checks, Azure's own view of the container, and recent server
  errors, public on purpose. Recorded in ADR: Observability.
- **Merges deploy themselves.** A green CI run on main builds the image,
  pushes it, and rolls the container with no human step and no stored
  secret, signed in through OIDC with least-privilege roles. Recorded in
  ADR: The deploy pipeline, under the CI/CD menu.
- **Every version gets its sentence.** One file, one line per shipped
  version, newest first, written by the commit that ships it and checked by
  the deploy that mints the number. Served as the Changelog menu; recorded in
  ADR: The changelog.
- **The docs show the code that runs.** A decision record's samples are
  read from this build's own source files at request time, so the words and
  the code cannot drift apart. Recorded in ADR: Live code samples.
- **Nothing stale reaches a browser.** Bundle files are named by their own
  contents and cached for a year; the page, the API and the documents say
  no-cache, so a new version shows on the next load on any device. Recorded
  in ADR: Cache headers.
- **Color is measured, not eyeballed.** Every text and ground pair in the
  palette is held to WCAG AA by a unit test that reads the tokens file, so
  a shade that fails contrast fails the build. Recorded in ADR: The
  palette.
- **The picture matches the words.** The infrastructure diagram on the
  Hosting page is drawn from the records, every surface carries a screenshot
  taken from the live site, code shown in a record counts as documented by
  it, and every diagram opens on its own page, zoomable, from a preview in
  the record. Recorded in ADR: Docs and testing and ADR: Diagram pages.
- **The code reviews itself before anyone else does.** A staff-level pass
  over each day's work, every finding written down as kept, fixed or
  deferred, and the fixes shipped with tests. Recorded in ADR: The staff
  review.
- **The configuration explains itself.** Three records walk Program.cs,
  the React configuration and the three test suites file by file at a new
  developer's level, the why beside every choice and the code read from
  the running build. Recorded in ADR: Program.cs, explained, ADR: The
  React configuration, explained and ADR: The tests, explained.
- **The failure nobody wrote code for still answers properly.** An unhandled
  exception returns the same problem shape as a rejected query, keeps the
  message and the stack trace inside, and hands back the trace id that finds
  the one log line holding both. An endpoint throws on purpose so that path
  can be exercised against the live container rather than assumed. Recorded
  in ADR: The exception handler.
- **Photos are sized for the box they land in.** The originals are 1280 wide
  and a card paints them at about 358, so a first load pulled 2.3 MB to fill
  nine boxes and a phone pulled exactly the same bytes as a desktop. There is a
  480-wide copy of every photo now, chosen by the browser through srcset, and a
  test holds the manifest and the image directory to the naming the browser
  relies on. Recorded in ADR: Responsive photos.
- **The accessibility rules a machine can check are checked on every run.**
  axe holds six views to WCAG 2.1 AA inside the browser suite. Its first run
  found two serious contrast failures on the busiest elements on the page, in
  a repository that already had a passing contrast test, because that test
  holds the colour pairs somebody listed and a stylesheet composes whatever it
  likes. Recorded in ADR: The accessibility check.
- **The storage moved without the application noticing.** The catalogue went
  from JSON files to SQLite through EF Core, with migrations applied at
  startup and a first boot that seeds itself, and not one line changed in the
  Application or Domain layers because both sat behind ports already. A bid
  now survives a restart, which a test proves by starting a second
  application against the same file. Recorded in ADR: The relational store
  and walked at a new developer's level in ADR: Entity Framework, explained.
- **The method is on the page, not implied.** How this was built, in the About
  menu, states that this was written with heavy AI assistance, names the
  criteria a reviewer should apply, and spends most of its length on the times
  the work was wrong: eleven defects found in a self review, a check that
  failed in CI on the command that had just passed locally, a guard that failed
  the day after it was chosen. Recorded in ADR: Saying how it was built.
- **The build cannot ship a version nothing describes.** The changelog's top
  line is where the deploy reads the version, so the footer and the file that
  documents the footer are the same string, and a ship that forgets its line
  fails rather than displaying a number with no sentence attached. Recorded
  in ADR: The version comes from the changelog.
