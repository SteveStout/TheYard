/**
 * Does:      Holds the home page's own words around its first screen and its close (ADR-082, the addendum on the home
 *            page's order; ADR-132, the addendum on the split hook), word for word from the home page's picture: the
 *            moves, the newest post's badge, Start here, the close's two lines and the dock's first button.
 * Does not:  Hold the hook's heading and sentences (data/hooks.json does, through pageHooks.ts), the name line (the
 *            review route's VERDICT.eyebrow, read by both pages) or draw anything; no React in here.
 * Used by:   Landing.tsx, NewestPost.tsx, landingMoves.test.ts.
 */
import { isNew } from './blog';

/** The first screen's moves: the primary button, the second and the text link. */
export const MOVES = {
  primary: 'Check my work in 2 minutes',
  resume: 'Resume (PDF)',
  inventory: 'or browse the live auction',
} as const;

/** The heading over the four doors. */
export const START_HERE = 'Start here';

/** The close's two lines on the home page; its buttons are the review route's close's own. */
export const LANDING_CLOSE = {
  readout: 'Next step',
  line: 'Twenty minutes, by email or on LinkedIn.',
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
