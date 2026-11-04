/**
 * The review route's Admin card (ADR: Ten minutes, seven stops, the addendum on the reach rows): the
 * funnel the API answers, the four windows, and the words the card prints for a stop, a time and the
 * day it started counting. A code says which link was sent, never who received it. No React in here.
 */
import { REVIEW_STOPS } from './reviewStops';

/** The windows the card offers, in the order its pills show them. */
export const REVIEW_WINDOWS = ['24h', '7d', '30d', '90d'] as const;

/** One of the four windows. */
export type ReviewWindow = (typeof REVIEW_WINDOWS)[number];

/** One tagged code's row, as the API answers it. */
export type ReviewCodeRow = { code: string; stop: number; minutes: number; last_day: string };

/** The card's report, as GET /api/admin/review answers it. */
export type ReviewFunnel = {
  available: boolean;
  window: string;
  opened: number;
  /** Eight counts: opened, then stops 1 to 7. */
  reached_by_stop: number[];
  by_code: ReviewCodeRow[];
  counted_from: string | null;
  source: string;
};

/** A window as its pill says it: "24 h", "7 d". */
export function windowLabel(window: ReviewWindow): string {
  return window.replace(/(\d+)([a-z])/, '$1 $2');
}

/** One bar of the funnel: its place (0 is opened), the words beside it and its count. */
export type FunnelBar = { place: number; name: string; count: number; share: number };

/** The eight bars: opened, then each stop by its number and title, each a share of the visitors who opened. */
export function funnelBars(funnel: Pick<ReviewFunnel, 'reached_by_stop'>): FunnelBar[] {
  const counts = Array.from({ length: REVIEW_STOPS.length + 1 }, (_, place) =>
    Math.max(0, funnel.reached_by_stop[place] ?? 0)
  );
  const most = Math.max(0, ...counts);
  return counts.map((count, place) => ({
    place,
    name: place === 0 ? 'Opened /review' : `${place} ${REVIEW_STOPS[place - 1].title}`,
    count,
    share: most === 0 || count === 0 ? 0 : Math.max(0.02, count / most),
  }));
}

/** How far a code got, in words: "Stop 7, The resume", or "Opened, left" for one that went no further. */
export function reachedWords(stop: number): string {
  if (stop <= 0 || stop > REVIEW_STOPS.length) return 'Opened, left';
  return `Stop ${stop}, ${REVIEW_STOPS[stop - 1].title}`;
}

/** How long a code's visitors took, in words: "under 1 min", "11 min". */
export function tookWords(minutes: number): string {
  return minutes < 1 ? 'under 1 min' : `${Math.round(minutes)} min`;
}

/** The line under the table: the day counting began, never a bare count read as all time. */
export function countedLine(countedFrom: string | null): string {
  return countedFrom === null
    ? 'Nothing is counted yet: the first visitor to open the route starts the count.'
    : `Counting from ${countedFrom}, the day version B shipped.`;
}
