/**
 * The review route (ADR: Ten minutes, seven stops): the one list the page at ?view=review draws,
 * the server and the browser alike. Seven stops in the order a reviewer asks about the site, each
 * one live thing to look at, its minutes and the one line its tour cell prints (ADR: The review
 * route gives a reason before it asks, the addendum on one unit). Every address is the site's own,
 * so the route never leaves the site; each stop's own page carries its reasoning. No React in here.
 */

/** One stop on the route. */
export type ReviewStop = {
  key: 'quality' | 'first-screen' | 'room' | 'spend' | 'gate' | 'stores' | 'resume';
  title: string;
  /** The address the stop opens, always the site's own ("/", "/?view=inventory", "/resume?pdf=1"). */
  address: string;
  minutes: number;
  /**
   * The tour cell's one line. "{vehicles}" and "{recommendations}" are the store's and the API's live counts, filled
   * by tourLine; nothing here is a typed number.
   */
  line: string;
};

/** The address the route is served at, a view like every other. */
export const REVIEW_ADDRESS = '/?view=review';

/** The label the Links slice records when a tagged link opens this view (TaggedRequests.PageViews). */
export const REVIEW_REPORT_PATH = '/review';

// #region review-stops
export const REVIEW_STOPS: readonly ReviewStop[] = [
  {
    key: 'quality',
    title: 'Quality you can watch',
    address: '/?doc=quality',
    minutes: 2,
    line: 'Built with AI, measured on every version.',
  },
  {
    key: 'first-screen',
    title: 'The first screen',
    address: '/',
    minutes: 1,
    line: 'Live answer time, the Azure bill, time since the last roll, visitors.',
  },
  {
    key: 'room',
    title: 'Bid in the room',
    address: '/?view=inventory',
    minutes: 2,
    line: '{vehicles} vehicles and a room that bids back.',
  },
  {
    key: 'spend',
    title: 'What Azure charges',
    address: '/?view=admin&card=spend',
    minutes: 1,
    line: 'The bill by resource, shown in public.',
  },
  {
    key: 'gate',
    title: 'The gate that shipped this version',
    address: '/?view=admin&card=tests',
    minutes: 1,
    line: 'Every suite, both stores, CodeRabbit on the pull request.',
  },
  {
    key: 'stores',
    title: 'One codebase, two stores',
    address: '/?doc=sql-vs-cosmos',
    minutes: 2,
    line: 'Azure SQL and Azure Cosmos DB, side by side.',
  },
  {
    key: 'resume',
    title: 'The resume, and who vouches for it',
    address: '/resume?pdf=1',
    minutes: 1,
    line: 'Two pages and {recommendations} named recommendations.',
  },
];
// #endregion review-stops

/**
 * A tour cell's line with the live counts in, each already in words ("100,000", "13"): "100,000 vehicles and a room
 * that bids back.", or before the store's count arrives "Vehicles and a room that bids back.", and "Two pages and the
 * named recommendations." before the API's.
 */
export function tourLine(
  stop: ReviewStop,
  counts: { vehicles: string; recommendations: string }
): string {
  return stop.line
    .replace(
      '{vehicles} vehicles',
      counts.vehicles === '' ? 'Vehicles' : `${counts.vehicles} vehicles`
    )
    .replace('{recommendations}', counts.recommendations === '' ? 'the' : counts.recommendations);
}
