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
- **Decisions get written down.** Nine ADRs record why the architecture is
  what it is, including reversed decisions, the production design that is
  deliberately left undeployed, and the documentation and testing rules
  themselves.
- **Infrastructure as code.** The production design lives in Bicep, served
  under the Hosting menu, deployable by flipping parameters.
- **Containers run hardened.** Multi-stage build, a non-root user, a real
  HTTP healthcheck, and no SDK in the runtime image.
- **The system reports on itself.** The Admin tab in the header shows live
  health checks, Azure's own view of the container, and recent server
  errors, public on purpose. Recorded in ADR: Observability.
