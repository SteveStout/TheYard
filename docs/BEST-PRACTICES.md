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
- **Decisions get written down.** Fifteen ADRs record why the architecture is
  what it is, including reversed decisions, the production design that is
  deliberately left undeployed, and the documentation and testing rules
  themselves.
- **Infrastructure as code.** The production design lives in Bicep, served
  under the Hosting menu, deployable by flipping parameters.
- **Containers run hardened.** Multi-stage build, a non-root user, a real
  HTTP healthcheck, and no SDK in the runtime image.
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
