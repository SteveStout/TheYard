/**
 * Connects the page's own controls to navigation: the button that opens the dialog, the close
 * button, the Files and Docs tabs, links that open a view, and the Escape key. Every one of them
 * calls navigate, so each change lands in the address bar like any other and Back undoes it.
 */
import { DEFAULTS, isOpen, VIEWS } from '../lib/urlState.js';
/**
 * Wires every control on the page to navigate.
 * @param page the elements index.html provides
 * @param navigate the one way the state changes
 * @param currentState reads the state the page is showing now
 */
export function connectControls(page, navigate, currentState) {
    // The button on the page opens the file browser.
    page.trigger.addEventListener('click', () => navigate({ view: 'browse' }));
    // Links marked with data-view open that view inside the dialog without reloading the page.
    // They stay real links with an href, so opening one in a new tab still works.
    for (const link of document.querySelectorAll('a[data-view]')) {
        link.addEventListener('click', (event) => {
            event.preventDefault();
            navigate({ view: viewOf(link.dataset['view']), doc: link.dataset['doc'] ?? '' });
        });
    }
    // The close button navigates to the closed state, so Back reopens the dialog where it was.
    page.closeButton.addEventListener('click', () => navigate({ ...DEFAULTS, open: false }));
    // The Files and Docs tabs. Switching away from Docs removes the open document from the
    // address, so a link copied from the file browser carries only what the file browser shows.
    page.tabs.addEventListener('click', (event) => {
        const button = event.target?.closest('button[data-view]');
        if (button) {
            const view = viewOf(button.dataset['view']);
            navigate(view === 'docs' ? { view } : { view, doc: '' });
        }
    });
    // The browser closes a modal dialog by itself when Escape is pressed. This listener then
    // updates the address to the closed state so the address and the dialog agree.
    page.dialog.addEventListener('close', () => {
        if (isOpen(currentState())) {
            navigate({ ...DEFAULTS, open: false });
        }
    });
}
/** Reads a view name from a link or tab, falling back to the default view for anything unknown. */
function viewOf(value) {
    return VIEWS.includes(value ?? '') ? value : DEFAULTS.view;
}
