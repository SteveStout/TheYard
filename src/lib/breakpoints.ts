/**
 * The site's width scale, the one list every media query is written from (the
 * styling pass of 25 September). Six steps, each the start of a layout:
 *
 *   480   a phone held upright is wider than a small phone's 375 to 479
 *   640   past a phone: the document dialog stops filling the screen, pills
 *         drop to their desk height, a panel's glass drops to the desk share
 *   768   a tablet: the Admin strip goes four across, the tables loosen
 *   1024  a desk: the site's rail docks, the vehicle takes two columns
 *   1280  a wide desk: the Admin rail stands beside the card, a pin gets a column
 *   1440  the widest: the hour beside its card, the store bar's whole sentence
 *
 * A stylesheet writes a step as `(min-width: 640px)` and the width under it as
 * `(max-width: 639.98px)`, so a fractional width under zoom (639.5) falls on one
 * side and never between two. StyleRulesTests holds every media query in src to
 * this list, and holds these queries to be the only ones a component asks from
 * script. The steps are in pixels because a media query cannot read a custom
 * property; tokens.css repeats the list in its comment.
 */
export const PHONE = '(max-width: 639.98px)';
export const DESK = '(min-width: 1024px)';
export const WIDE = '(min-width: 1280px)';
export const WIDEST = '(min-width: 1440px)';
