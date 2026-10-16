/**
 * The elements the page cannot run without. They are written by hand in wwwroot/index.html, so a
 * missing one is a mistake in that file, and finding them all at start-up names it at once.
 */

/** Every element the rest of the page works with, found once at start-up. */
export interface PageElements {
  /** The dialog the file browser and the Docs tab live in. */
  readonly dialog: HTMLDialogElement;
  /** The button on the page that opens the dialog. */
  readonly trigger: HTMLButtonElement;
  /** The Files and Docs tabs at the top of the dialog. */
  readonly tabs: HTMLElement;
  /** Where the file browser draws itself. */
  readonly browserRoot: HTMLElement;
  /** Where the Docs tab draws itself. */
  readonly documentationRoot: HTMLElement;
  /** The button that closes the dialog. */
  readonly closeButton: HTMLButtonElement;
  /** The page footer, where the build version is shown. */
  readonly footer: HTMLElement;
}

/** Finds every element the page needs, or throws naming the first one that is missing. */
export function findPageElements(): PageElements {
  return {
    dialog: need<HTMLDialogElement>('dialog.shed'),
    trigger: need<HTMLButtonElement>('#open-shed'),
    tabs: need<HTMLElement>('#tabs'),
    browserRoot: need<HTMLElement>('#browser'),
    documentationRoot: need<HTMLElement>('#docs'),
    closeButton: need<HTMLButtonElement>('#close-shed'),
    footer: need<HTMLElement>('#version'),
  };
}

/** Finds one element by its CSS selector, or throws an error that names the selector. */
function need<T extends Element>(selector: string): T {
  const element = document.querySelector<T>(selector);
  if (!element) {
    throw new Error(`index.html has no ${selector}.`);
  }
  return element;
}
