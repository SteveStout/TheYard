/**
 * The page: reads the address, opens the dialog when the address says so, and
 * hands the state to whichever view it names (ADR-005, ADR-007). This is the
 * only file that touches history; every view asks for a change through
 * navigate() and is redrawn from the address that results, the same path a Back
 * button or a pasted link takes.
 */
import * as api from './lib/api.js';
import { DEFAULTS, isOpen, parse, serialize, VIEWS } from './lib/urlState.js';
import { createBrowser } from './ui/browser.js';
import { createDocs } from './ui/docs.js';
import { h, replace } from './ui/dom.js';
/** An element the page cannot work without; index.html is the only place it comes from. */
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
const docsRoot = need('#docs');
const closeButton = need('#close-shed');
const footer = need('#version');
let state = parse(window.location.search);
const browser = createBrowser(browserRoot, navigate);
const docs = createDocs(docsRoot, navigate);
// #region navigate
/**
 * The one way the state changes: merge, write the address, redraw. Closing is
 * navigating to the defaults, so Back from a closed page reopens it where it was.
 * @param partial the keys that change
 * @param replaceEntry true to replace the history entry (typing in the search box)
 */
function navigate(partial, replaceEntry = false) {
    const next = { ...state, open: true, ...partial };
    // A closed state serializes to "", and pushState needs an address, so the bare path stands in.
    const url = serialize(next) || window.location.pathname;
    if (replaceEntry) {
        window.history.replaceState(null, '', url);
    }
    else {
        window.history.pushState(null, '', url);
    }
    // Re-read from the address rather than trusting `next`: the browser is the one source, and a
    // value the serializer dropped (a default) must not survive in memory either.
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
 * Draws the page from the state and nothing else. The dialog's open flag is a
 * fact about the address (ADR-007): closed when no known key is present, open
 * otherwise, so a pasted link and the Back button land in the same place as a
 * click. showModal rather than show, because a modal gives the focus trap,
 * Escape, the backdrop and an inert page for free.
 */
function render() {
    if (!isOpen(state)) {
        if (dialog.open) {
            dialog.close();
            trigger.focus(); // Back where the person started, with the keyboard.
        }
        return;
    }
    if (!dialog.open) {
        dialog.showModal();
    }
    // The tabs are buttons with aria-pressed, not links, because they change a view inside one
    // page; the address still changes, through navigate, so a tab is a link in every way that counts.
    for (const button of tabs.querySelectorAll('button')) {
        button.setAttribute('aria-pressed', button.dataset['view'] === state.view ? 'true' : 'false');
    }
    browserRoot.hidden = state.view !== 'browse';
    docsRoot.hidden = state.view !== 'docs';
    void (state.view === 'docs' ? docs.render(state) : browser.render(state));
}
// #endregion render
trigger.addEventListener('click', () => navigate({ view: 'browse' }));
// Links on the page that open a view do it in place; as plain links they still work in a new tab.
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
        // Leaving Docs drops the document from the address, so a link copied from the Files tab
        // says only what the Files tab shows.
        const view = viewOf(button.dataset['view']);
        navigate(view === 'docs' ? { view } : { view, doc: '' });
    }
});
// Escape closes a modal dialog on its own; the address has to follow it.
dialog.addEventListener('close', () => {
    if (isOpen(state)) {
        navigate({ ...DEFAULTS, open: false });
    }
});
api.version().then((version) => {
    // A real link, so it works with the keyboard and in a new tab; clicked in place it navigates without a reload.
    const about = h('a', { href: '?view=docs&doc=readme', onclick: (event) => { event.preventDefault(); navigate({ view: 'docs', doc: 'readme' }); } }, 'about this build');
    replace(footer, `The Shed ${version.version} @ ${version.commit}`, ' · ', about);
}).catch(() => replace(footer, 'The Shed'));
render();
