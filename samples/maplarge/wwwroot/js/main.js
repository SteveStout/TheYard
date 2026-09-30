/**
 * Entry point for the page. It reads the page address (the query string), opens
 * the file browser dialog when the address asks for it, and passes the parsed
 * state to the view the address names: the file browser or the Docs tab.
 *
 * This is the only file that calls the browser history API. Views never change
 * the address themselves; they call navigate(), which writes the new address and
 * then redraws the page from it. The Back button and a pasted link also redraw
 * from the address, so all three ways of reaching a view go through the same
 * code and cannot drift apart. (More in docs/ADR-005-state-lives-in-the-url.md and
 * docs/ADR-007-the-dialog-widget.md.)
 */
import * as api from './lib/api.js';
import { DEFAULTS, isOpen, parse, serialize, VIEWS } from './lib/urlState.js';
import { createBrowser } from './ui/browser.js';
import { createDocumentation } from './ui/documentation.js';
import { buildElement, replaceContents } from './ui/elements.js';
/**
 * Finds an element the page cannot run without, or throws. These elements are
 * written by hand in index.html, so a missing one is a mistake in that file and
 * failing loudly at start-up names it at once.
 */
function need(selector) {
    const element = document.querySelector(selector);
    if (!element) {
        throw new Error(`index.html has no ${selector}.`);
    }
    return element;
}
function viewOf(value) {
    return VIEWS.includes(value ?? '') ? value : DEFAULTS.view;
}
const dialog = need('dialog.shed');
const trigger = need('#open-shed');
const tabs = need('#tabs');
const browserRoot = need('#browser');
const documentationRoot = need('#docs');
const closeButton = need('#close-shed');
const footer = need('#version');
let state = parse(window.location.search);
const browser = createBrowser(browserRoot, navigate);
const documentation = createDocumentation(documentationRoot, navigate);
// #region navigate
/**
 * Changes the page state. It merges the changed keys into the current state,
 * writes the result into the address bar as a history entry, then redraws.
 * Every state change in the app comes through here. Closing the dialog is also
 * a navigation (to the default state), so pressing Back after closing reopens
 * the dialog where it was.
 * @param partial the state keys that change; keys left out keep their current value
 * @param replaceEntry true to overwrite the current history entry instead of adding one
 *   (used while typing in the search box, so each keystroke is not its own Back step)
 */
function navigate(partial, replaceEntry = false) {
    const next = { ...state, open: true, ...partial };
    // A closed state serializes to an empty string. pushState with "" would keep the old query
    // string, so the page path with no query string is written instead, which clears it.
    const url = serialize(next) || window.location.pathname;
    if (replaceEntry) {
        window.history.replaceState(null, '', url);
    }
    else {
        window.history.pushState(null, '', url);
    }
    // Parse the address again instead of keeping `next`. The address is the only source of truth,
    // and parsing it also cleans up any value the serializer left out or would have rejected.
    state = parse(window.location.search);
    render();
}
window.addEventListener('popstate', () => {
    state = parse(window.location.search);
    render();
});
// #endregion navigate
// #region render
/**
 * Draws the page from the current state and nothing else. The dialog is closed
 * when the address has no known key and open otherwise, so a pasted link, the
 * Back button and a click all end up showing the same thing.
 * It uses showModal rather than show because a modal dialog keeps keyboard focus
 * inside itself, closes on Escape, draws a backdrop and makes the rest of the
 * page inert, with no extra code. (More in docs/ADR-007-the-dialog-widget.md.)
 */
function render() {
    if (!isOpen(state)) {
        if (dialog.open) {
            dialog.close();
            trigger.focus(); // Return keyboard focus to the button that opened the dialog.
        }
        return;
    }
    if (!dialog.open) {
        dialog.showModal();
    }
    // Mark the tab for the current view as pressed. The tabs are buttons with aria-pressed because
    // they switch a view inside one page. Clicking one still calls navigate, so the address changes
    // and the view can be bookmarked or shared like a link.
    for (const button of tabs.querySelectorAll('button')) {
        button.setAttribute('aria-pressed', button.dataset['view'] === state.view ? 'true' : 'false');
    }
    browserRoot.hidden = state.view !== 'browse';
    documentationRoot.hidden = state.view !== 'docs';
    void (state.view === 'docs' ? documentation.render(state) : browser.render(state));
}
// #endregion render
trigger.addEventListener('click', () => navigate({ view: 'browse' }));
// Links marked with data-view open that view inside the dialog without reloading the page.
// They stay real links with an href, so opening one in a new tab still works.
for (const link of document.querySelectorAll('a[data-view]')) {
    link.addEventListener('click', (event) => {
        event.preventDefault();
        navigate({ view: viewOf(link.dataset['view']), doc: link.dataset['doc'] ?? '' });
    });
}
closeButton.addEventListener('click', () => navigate({ ...DEFAULTS, open: false }));
tabs.addEventListener('click', (event) => {
    const button = event.target?.closest('button[data-view]');
    if (button) {
        // Switching away from Docs removes the open document from the address, so a link copied
        // from the file browser carries only what the file browser shows.
        const view = viewOf(button.dataset['view']);
        navigate(view === 'docs' ? { view } : { view, doc: '' });
    }
});
// The browser closes a modal dialog by itself when Escape is pressed. This listener then
// updates the address to the closed state so the address and the dialog agree.
dialog.addEventListener('close', () => {
    if (isOpen(state)) {
        navigate({ ...DEFAULTS, open: false });
    }
});
api.version().then((version) => {
    // Show the build version in the footer with a link to the readme. It is a real link, so it
    // works from the keyboard and in a new tab; a normal click navigates without a reload.
    const about = buildElement('a', { href: '?view=docs&doc=readme', onclick: (event) => { event.preventDefault(); navigate({ view: 'docs', doc: 'readme' }); } }, 'about this build');
    replaceContents(footer, `The Shed ${version.version} @ ${version.commit}`, ' · ', about);
}).catch(() => replaceContents(footer, 'The Shed'));
render();
