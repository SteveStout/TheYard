# ADR: The dialog widget

Status: accepted, 2026-09-29.

## Context

The first bonus: "entire component contained in a dialog widget, with a trigger element." A dialog
that is also deep-linkable (ADR-005) has to answer a question the bonus does not ask: what does a
link to a view inside the dialog do when the dialog is closed?

## Decision

The browser lives in a native `<dialog>` and nothing else is built for it. `showModal()` gives the
focus trap, the Escape key, the backdrop and the inert page behind for free, which is what a
library would be for. The trigger is one button on the page.

The dialog's open state is the address (ADR-005): open when the query string carries any known key,
closed when it carries none. So a shared link opens straight into the view it names, the trigger
writes `?view=browse`, the close button and Escape write the bare address, and Back from a closed
page reopens it. The `close` event the browser fires for Escape is caught and turned into the same
navigation as the button, so there is one path out.

Inside, two tabs switch `view` between the files and the documents; both sections are always in the
page and one is `hidden`, so switching costs nothing and each keeps its own scroll.

Focus goes to the trigger when the dialog closes, so a keyboard user lands where they started.
Every action in a row is a real button with an accessible name that includes the file's name
("Delete readme.md"), the table's sort state is `aria-sort`, and the totals line is a live region
so a screen reader hears counts change.

## What it cost

`showModal()` needs the dialog to be closed to be called, and `close()` needs it open, so the
render checks `dialog.open` before either. The one browser behaviour worth knowing: a form with
`method="dialog"` would close it on submit, so none of the forms inside use that method.

## Files

- [`wwwroot/index.html`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/wwwroot/index.html): the dialog and the trigger.
- [`wwwroot/js/main.js`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/wwwroot/js/main.js): open and close as navigation.
- [`wwwroot/css/app.css`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/wwwroot/css/app.css): the dialog's panel, full screen on a phone.

The render that opens and closes it:

```live path=wwwroot/js/main.js region=render
```

Its styles:

```live path=wwwroot/css/app.css region=dialog
```
