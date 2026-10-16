/**
 * The elements the page cannot run without. They are written by hand in wwwroot/index.html, so a
 * missing one is a mistake in that file, and finding them all at start-up names it at once.
 */
/** Finds every element the page needs, or throws naming the first one that is missing. */
export function findPageElements() {
    return {
        dialog: need('dialog.shed'),
        trigger: need('#open-shed'),
        tabs: need('#tabs'),
        browserRoot: need('#browser'),
        documentationRoot: need('#docs'),
        closeButton: need('#close-shed'),
        footer: need('#version'),
    };
}
/** Finds one element by its CSS selector, or throws an error that names the selector. */
function need(selector) {
    const element = document.querySelector(selector);
    if (!element) {
        throw new Error(`index.html has no ${selector}.`);
    }
    return element;
}
