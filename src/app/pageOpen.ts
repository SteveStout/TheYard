/**
 * Does:      Names the pages that cover the rest of the app (the blog, the review route, the recommendations, the stack,
 *            About Steven) and says which one is open.
 * Does not:  Keep the open flags (useAddressBar.ts does) or close anything.
 * Used by:   useAddressBar.ts, firstAddress.test.ts.
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
