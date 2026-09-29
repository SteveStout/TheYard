// The page: reads the address, opens the dialog when the address says so, and
// hands the state to whichever view it names (ADR-005, ADR-007). This is the
// only file that touches history; every view asks for a change through
// navigate() and is redrawn from the address that results, the same path a Back
// button or a pasted link takes.

import * as api from './lib/api.js';
import { DEFAULTS, isOpen, parse, serialize } from './lib/urlState.js';
import { createBrowser } from './ui/browser.js';
import { createDocs } from './ui/docs.js';
import { h, replace } from './ui/dom.js';

const dialog = document.querySelector('dialog.shed');
const trigger = document.querySelector('#open-shed');
const tabs = document.querySelector('#tabs');
const browserRoot = document.querySelector('#browser');
const docsRoot = document.querySelector('#docs');
const closeButton = document.querySelector('#close-shed');
const footer = document.querySelector('#version');

let state = parse(window.location.search);
const browser = createBrowser(browserRoot, navigate);
const docs = createDocs(docsRoot, navigate);

// #region navigate
/**
 * The one way the state changes: merge, write the address, redraw. Closing is
 * navigating to the defaults, so Back from a closed page reopens it where it was.
 * @param {object} partial the keys that change
 * @param {boolean} [replaceEntry] true to replace the history entry (typing in the search box)
 */
function navigate(partial, replaceEntry = false) {
  const next = { ...state, open: true, ...partial };
  const url = serialize(next) || window.location.pathname;
  if (replaceEntry) {
    window.history.replaceState(null, '', url);
  } else {
    window.history.pushState(null, '', url);
  }
  state = parse(window.location.search);
  render();
}

window.addEventListener('popstate', () => {
  state = parse(window.location.search);
  render();
});
// #endregion navigate

// #region render
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
  for (const button of tabs.querySelectorAll('button')) {
    button.setAttribute('aria-pressed', button.dataset.view === state.view ? 'true' : 'false');
  }
  browserRoot.hidden = state.view !== 'browse';
  docsRoot.hidden = state.view !== 'docs';
  if (state.view === 'docs') {
    docs.render(state);
  } else {
    browser.render(state);
  }
}
// #endregion render

trigger.addEventListener('click', () => navigate({ view: 'browse' }));
closeButton.addEventListener('click', () => navigate({ ...DEFAULTS, open: false }));
tabs.addEventListener('click', (event) => {
  const button = event.target.closest('button[data-view]');
  if (button) {
    navigate({ view: button.dataset.view });
  }
});
// Escape closes a modal dialog on its own; the address has to follow it.
dialog.addEventListener('close', () => {
  if (isOpen(state)) {
    navigate({ ...DEFAULTS, open: false });
  }
});

api.version().then((version) => {
  replace(footer, `The Shed ${version.version} @ ${version.commit}`, ' · ', h('a', { href: '?view=docs&doc=readme' }, 'about this build'));
}).catch(() => replace(footer, 'The Shed'));

render();
