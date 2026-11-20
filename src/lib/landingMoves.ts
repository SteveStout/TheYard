/**
 * Does:      Holds the home page's own words around its first screen and its close (ADR-082, the addendum on the home
 *            page's order; ADR-132, the addendum on the split hook; ADR: The sales pages set the site's look), word for
 *            word from the home page's picture: the four first moves, the newest post's badge, Start here, the numbers
 *            of the page's sections, the close (the review route's closing question under its own 08) and the dock's
 *            first button.
 * Does not:  Hold the hook's heading and sentences (data/hooks.json does, through pageHooks.ts), the name line (the
 *            review route's VERDICT.eyebrow, read by both pages) or draw anything; no React in here.
 * Used by:   Landing.tsx, LandingVoices.tsx, LatestPosts.tsx, NewestPost.tsx, landingMoves.test.ts.
 */
import { isNew } from './blog';
import { CLOSE } from './reviewVerdict';

/** The first screen's moves, in the row's order: the primary button, the gold resume, the booking and the text link. */
export const MOVES = {
  primary: 'Check my work in 2 minutes',
  resume: 'Resume (PDF)',
  book: 'Book 20 minutes',
  inventory: 'or browse the live auction',
} as const;

/** The heading over the four doors. */
export const START_HERE = 'Start here';

/** Each section's place on the page, drawn as the shared section head's badge, top to bottom as the picture numbers them. */
export const SECTION_NUMBERS = {
  start: '01',
  posts: '02',
  voices: '03',
  stack: '04',
  built: '05',
  run: '06',
  who: '07',
  close: '08',
} as const;

/**
 * The close on the home page asks the review route's closing question under its own number; the heading a screen
 * reader lands on still says "Next step". Its buttons are the review route's close's own.
 */
export const LANDING_CLOSE = {
  label: CLOSE.label,
  number: SECTION_NUMBERS.close,
  question: CLOSE.question,
  line: CLOSE.line,
} as const;

/** The dock's first button on the home page; the second is the resume, as on the review route. */
export const DOCK_REVIEW = 'Check my work';

/** The link under the three voices, with the answer's own count, or without one before the answer. */
export function readAllLine(count: number | null): string {
  return count === null
    ? 'Read all named recommendations'
    : `Read all ${count} named recommendations`;
}

/** The newest post's badge: "New post" while the sidebar's own rule calls it new (under a week old), else "Latest post". */
export function newestBadge(publishedAt: string | undefined, nowMs: number): string {
  return isNew(publishedAt, nowMs) ? 'New post' : 'Latest post';
}
