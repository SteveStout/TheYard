/**
 * Does:      Names the pages that cover the rest of the app (the blog, the review route, the recommendations, the stack,
 *            About Steven), says which one is open, and says whether the view on screen is a landing view: one of the
 *            six a recruiter link opens, which share the review route's shell (ADR-142, the addendum on the landing views).
 * Does not:  Keep the open flags (useAddressBar.ts does), close anything, or draw the shell (Shell.tsx does).
 * Used by:   useAddressBar.ts, firstAddress.test.ts, pageOpen.test.ts.
 */

/** A page that covers the rest of the app. */
export type PageKey = 'blog' | 'review' | 'recommendations' | 'stack' | 'about';

/** The page open, or null when none is: one at a time, in this order if the flags ever disagree. */
export const pageOf = (
  blog: boolean,
  review: boolean,
  recommendations: boolean,
  stack = false,
  about = false
): PageKey | null =>
  blog
    ? 'blog'
    : review
      ? 'review'
      : recommendations
        ? 'recommendations'
        : stack
          ? 'stack'
          : about
            ? 'about'
            : null;

/** The pages that are landing views: with home, the six a recruiter link opens. The stack is a page, not a landing. */
export const LANDING_PAGES: readonly PageKey[] = ['review', 'about', 'blog', 'recommendations'];

/**
 * Whether the view on screen is a landing view, so the rail starts closed and the band is the review route's live
 * sentence: home (nothing open over it), or a landing page with no Admin or Account view drawn in its place.
 * @param view the open page, whether home shows, and the two views drawn ahead of every page
 */
export const landingOf = (view: {
  page: PageKey | null;
  homeShowing: boolean;
  adminOpen: boolean;
  accountOpen: boolean;
}): boolean =>
  view.page === null
    ? view.homeShowing
    : LANDING_PAGES.includes(view.page) && !view.adminOpen && !view.accountOpen;
