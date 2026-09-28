/**
 * The site's width scale: the one list every media query is written from.
 * Six steps, each the width where a layout changes:
 *
 *   480   a phone held upright, wider than a small phone
 *   640   past a phone: a document stops filling the screen, pills drop to
 *         their desk height, a panel's glass drops to the desk share
 *   768   a tablet: the Admin strip goes four across, the tables loosen
 *   1024  a desk: the site's rail docks, a vehicle takes two columns
 *   1280  a wide desk: the Admin rail stands beside the card, a pin gets a column
 *   1440  the widest: the hour beside its card, the store bar's whole sentence
 *
 * In a stylesheet, write a step as `(min-width: 640px)` and the width under
 * it as `(max-width: 639.98px)`. The .02 gap means a fractional width under
 * zoom (639.5) always lands on one side, never between the two.
 *
 * The steps are in pixels here, not in tokens.css, because a media query
 * cannot read a CSS variable. StyleRulesTests (rule ten) fails the build on
 * any media query off this list, and on a component that asks for a width
 * without importing one of these constants.
 */
export const PHONE = '(max-width: 639.98px)';
export const DESK = '(min-width: 1024px)';
export const WIDE = '(min-width: 1280px)';
export const WIDEST = '(min-width: 1440px)';
