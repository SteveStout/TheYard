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
- **Decisions get written down.** Twenty-six ADRs record why the architecture is
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
