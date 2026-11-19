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

/**
 * The card's two figures (ADR: Ten minutes, seven stops, the addendum on the second actions), each a count of the
 * visitors who opened: any second action or a stop past the opening, and the running system or the booking button.
 */
export type ReviewSecond = {
  second_action: number;
  running_or_book: number;
  counted_from: string;
  /** The same two figures for each first screen; absent from an answer made before screens were kept. */
  by_screen?: ReviewScreenRow[];
};

/** One first screen's row: who opened on it and how many of them reached each figure. */
export type ReviewScreenRow = {
  screen: string;
  opened: number;
  second_action: number;
  running_or_book: number;
};

/** What the card calls each first screen, and where it is. */
const SCREEN_NAMES: Record<string, string> = {
  verdict: 'The verdict, /?view=review',
  mirror: 'The mirror, /?view=review&for=ai',
};

/** One screen's line on the card: its name, who opened on it, and the two figures as shares of them. */
export function screenLine(row: ReviewScreenRow): string {
  const share = (count: number) =>
    row.opened === 0 ? 0 : Math.round((Math.min(count, row.opened) * 100) / row.opened);
  return `${SCREEN_NAMES[row.screen] ?? row.screen}: ${row.opened} opened, ${share(row.second_action)}% a second action, ${share(row.running_or_book)}% the running system or the booking button`;
}

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
  /** Absent from an answer made before the actions were counted, which reads as none. */
  second?: ReviewSecond;
};

/** One of the card's two figures: what it counts, how many and their share of the visitors who opened. */
export type SecondFigure = { key: string; label: string; count: number; percent: number };

/** The two figures for a window, each as a count and a whole percent of the visitors who opened, never above 100. */
export function secondFigures(funnel: Pick<ReviewFunnel, 'opened' | 'second'>): SecondFigure[] {
  const opened = Math.max(0, funnel.opened);
  const figure = (key: string, label: string, raw: number | undefined): SecondFigure => {
    const count = Math.min(opened, Math.max(0, raw ?? 0));
    return { key, label, count, percent: opened === 0 ? 0 : Math.round((count * 100) / opened) };
  };
  return [
    figure('second', 'reached a second action', funnel.second?.second_action),
    figure(
      'running',
      'reached the running system or the booking button',
      funnel.second?.running_or_book
    ),
  ];
}

/** A figure in words: "3 of 10 who opened, 30%". */
export function figureWords(figure: SecondFigure, opened: number): string {
  return `${figure.count} of ${Math.max(0, opened)} who opened, ${figure.percent}%`;
}

/** The line under the two figures: the day they started counting. */
export function secondCountedLine(second: ReviewSecond | undefined): string {
  return second === undefined
    ? 'The two figures are not counted on this version.'
    : `These two figures count from ${second.counted_from}, the day the buttons started reporting.`;
}

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

/**
 * The day the stops were reordered, when Quality you can watch became stop 1 and One decision, with its code
 * folded into it. Reach is kept by stop number, so a visit counted before this day shows under the new order's
 * names; the card says so rather than relabel the kept rows.
 */
export const REVIEW_REORDERED = '2026-11-11';

/** The line under the counting line, naming the day the order changed. */
export const REORDERED_LINE = `The stops were reordered on ${REVIEW_REORDERED}, when Quality you can watch became stop 1. Reach before that day was counted against the earlier order.`;
