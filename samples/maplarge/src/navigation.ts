/**
 * The one way the page changes. The current state is always read from the address bar; navigate()
 * changes it and render() draws it. This is the only file that calls the browser history API.
 * Views never change the address themselves; they call navigate(), which writes the new address
 * and then redraws the page from it. The Back button and a pasted link also redraw from the
 * address, so all three ways of reaching a view go through the same code and cannot drift apart.
 * (More in docs/ADR-005-state-lives-in-the-url.md and docs/ADR-007-the-dialog-widget.md.)
 */

import { isOpen, parse, serialize, type State } from './lib/urlState.js';
import { createBrowser, type View } from './ui/browser.js';
import { createDocumentation } from './ui/documentation.js';
import type { PageElements } from './ui/pageElements.js';

let page: PageElements;
let state: State;
let browser: View;
let documentation: View;

/**
 * Reads the address, creates the two views (the file browser and the Docs tab), and starts
 * following the Back and Forward buttons. Called once, at start-up.
 * @param elements the elements index.html provides
 */
export function startNavigation(elements: PageElements): void {
  page = elements;
  state = parse(window.location.search);
  browser = createBrowser(page.browserRoot, navigate);
  documentation = createDocumentation(page.documentationRoot, navigate);
  // Back and Forward change the address without calling navigate, so the page redraws from it here.
  window.addEventListener('popstate', () => {
    state = parse(window.location.search);
    render();
  });
}

/** The state the page is showing now, as last read from the address bar. */
export function currentState(): State {
  return state;
}

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
export function navigate(partial: Partial<State>, replaceEntry = false): void {
  const next: State = { ...state, open: true, ...partial };
  // A closed state serializes to an empty string. pushState with "" would keep the old query
  // string, so the page path with no query string is written instead, which clears it.
  const url = serialize(next) || window.location.pathname;
  if (replaceEntry) {
    window.history.replaceState(null, '', url);
  } else {
    window.history.pushState(null, '', url);
  }
  // Parse the address again instead of keeping `next`. The address is the only source of truth,
  // and parsing it also cleans up any value the serializer left out or would have rejected.
  state = parse(window.location.search);
  render();
}
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
export function render(): void {
  if (!isOpen(state)) {
    if (page.dialog.open) {
      page.dialog.close();
      page.trigger.focus(); // Return keyboard focus to the button that opened the dialog.
    }
    return;
  }
  if (!page.dialog.open) {
    page.dialog.showModal();
  }
  // Mark the tab for the current view as pressed. The tabs are buttons with aria-pressed because
  // they switch a view inside one page. Clicking one still calls navigate, so the address changes
  // and the view can be bookmarked or shared like a link.
  for (const button of page.tabs.querySelectorAll<HTMLButtonElement>('button')) {
    button.setAttribute('aria-pressed', button.dataset['view'] === state.view ? 'true' : 'false');
  }
  page.browserRoot.hidden = state.view !== 'browse';
  page.documentationRoot.hidden = state.view !== 'docs';
  void (state.view === 'docs' ? documentation.render(state) : browser.render(state));
}
// #endregion render
