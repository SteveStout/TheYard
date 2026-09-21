/**
 * The intro strip over the inventory (ADR: The glass look, the addendum on
 * who the first screen is for). Somebody who tapped a link in a post lands on
 * a phone with about ten seconds, and the first screen was a store switch and
 * a column of filters: nothing said what this is, who built it or where the
 * resume is. One sentence and its links say it, and the strip goes away for
 * good when it is dismissed. The second link, to the person who built it,
 * came with the Author page (ADR: The sidebar, the addendum on the author's
 * section).
 *
 * No React in here: the words, the places the links go, and the one
 * remembered fact, behind a storage that may be missing or blocked.
 */
export const INTRO = {
  sentence:
    'Steven Stout’s working demo: a used-vehicle auction site on .NET and React, 100,000 vehicles, live on Azure, with its decisions written down.',
  links: [
    { key: 'resume', label: 'Resume (PDF)' },
    { key: 'author', label: 'Who built this' },
    { key: 'built', label: 'How it is built' },
    { key: 'admin', label: 'Live Admin tab' },
  ],
  dismiss: 'Dismiss the introduction',
} as const;

export type IntroLink = (typeof INTRO.links)[number]['key'];

const INTRO_KEY = 'theyard.intro';

type Store = Pick<Storage, 'getItem' | 'setItem'>;

/** Whether the strip was dismissed in this browser; a missing or blocked store means it was not. */
export function introDismissed(store: Store | undefined): boolean {
  try {
    return store?.getItem(INTRO_KEY) === 'dismissed';
  } catch {
    return false;
  }
}

/** Remember the dismissal; a store that refuses is a strip that comes back, which is the honest failure. */
export function dismissIntro(store: Store | undefined): void {
  try {
    store?.setItem(INTRO_KEY, 'dismissed');
  } catch {
    // Nothing to do: the strip is still gone from this page.
  }
}
