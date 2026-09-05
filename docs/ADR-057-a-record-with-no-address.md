# ADR: A record with no address

Status: accepted, 2026-09-04. Fifty-six records, and no way to send anybody one
of them.

## Context

This project's argument is its decision records. They are what a reviewer is
pointed at, they are what the README leads with, and there are enough of them
that the sidebar gives them a numbered index of their own.

Until now, the only way to reach one was: open the site, find the Decision
Records group, expand it, scroll, click. That is fine for somebody browsing and
useless for every other way a record actually gets read:

- "The lockout reasoning is in ADR-050" in an interview, with no link to send.
- A record cited from another record, from the README, or from a commit message.
- A tab kept open on a record, reloaded, landing back on the inventory.
- Anything shared in a message, which is how a portfolio actually travels.

Every other view here already had an address. `?vehicle={id}` opens a detail
page, `?view=admin` and `?view=account` open theirs, and the filters and the
sort are in the query string, which is what made the sitemap possible in the
first place. The documents were the exception, and only because the dialog that
shows them happens to be owned by the sidebar rather than by the page.

## Decision

`?doc={slug}`, and the slug is the one the API already serves the document at.

**The slug is derived, not written twice.** `docSlug` reads it off the entry's
own `/api/docs/{slug}` URL, so a document cannot become linkable under a name
the API does not answer to. A .NET test already holds the menu's slugs and the
catalogue's to each other, which means the address in the browser and the file
on disk are the same list checked in three places.

**An address that names nothing opens nothing.** `?doc=not-a-record` lands on
the inventory rather than on an error. A link that has gone stale should not
greet a reader with a failure; it should show them the thing the link was
attached to.

**Opening pushes, closing goes back.** Opening a record pushes a history entry,
exactly as opening a vehicle does, so the browser's Back button closes it. And
because the dialog can also be closed with Escape, the X, or the backdrop,
closing checks whether we pushed the entry that opened it: if so it goes back,
so Back and Escape agree and the history stays clean; if not, which is what a
visitor arriving on the link gets, it swaps the parameter out in place rather
than navigating them off the site.

**The state moved up to App.** The dialog used to own which record was showing,
which was right while the only way to open or close one was the dialog itself.
An address changes that: the browser can now close a record without the dialog
being touched, so the dialog had to learn to follow the state instead of being
it. That is one effect in the dialog, closing itself when there is no record,
and it is the whole cost of the move.

## Alternatives

**A path, `/docs/adr-lockout`.** Nicer to read and it needs the origin to serve
`index.html` for a path it does not know, which is a rewrite rule in the edge
and in the container and a thing to get wrong at deploy time. Every other view
here is a parameter; this one matching them is worth more than a prettier URL.

**Link straight at the API, `/api/docs/adr-lockout`.** That already works and
returns raw markdown. It is the right link for a machine and the wrong one for
a person, who gets a wall of asterisks instead of the site.

**A separate page per record.** ADR-020 did exactly that for diagrams, because
a diagram wants the whole viewport and its own zoom. A record is text in a
dialog over the page that sent you to it, and it is one Escape from being back
where you were.

## Consequences

- Every record has a link that can be pasted anywhere, and the link survives a
  reload, a share, and somebody else's browser.
- Back closes a record. So does Escape. They no longer disagree.
- A stale link lands on the inventory rather than an error.
- The sidebar no longer owns which document is showing, which makes it one
  fewer place where the address bar and the page can drift apart.

## Files

- [`src/components/DocsMenu.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/DocsMenu.tsx): the slug both directions, and the dialog that now follows the state.
- [`src/App.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/App.tsx): the address bar, the pushed entry, and the close that knows whether it pushed one.
- [`src/components/SideNav.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/SideNav.tsx): what is left of it, which is turning a key into a request.
- [`tests/e2e/records.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/records.spec.ts): the link, the Back button, the keyboard, and the address that names nothing.
- [`api/TheYard.Tests/DocsCatalogTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/DocsCatalogTests.cs): the slugs the address bar borrows, held to the catalogue.
