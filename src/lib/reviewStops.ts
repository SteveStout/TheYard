/**
 * The review route (ADR: Ten minutes, seven stops): the one list the page at ?view=review draws,
 * the server and the browser alike. Seven stops in the order a reviewer asks about the site, each
 * one live thing to look at, one line on what it shows and one thing to do. Every address is the
 * site's own, every stop names the decision record behind it, and the only way out is the GitHub
 * link on stop 5's card. No React in here.
 */

/** One stop on the route. */
export type ReviewStop = {
  key: 'first-screen' | 'room' | 'spend' | 'decision' | 'gate' | 'stores' | 'resume';
  title: string;
  /** The address the stop opens, always the site's own ("/", "/?view=inventory", "/resume?pdf=1"). */
  address: string;
  minutes: number;
  shows: string;
  doThis: string;
  /** The decision record behind the stop, a catalogue slug, opened at /?doc=<slug>. */
  record: string;
};

/** The address the route is served at, a view like every other. */
export const REVIEW_ADDRESS = '/?view=review';

/** The label the Links slice records when a tagged link opens this view (TaggedRequests.PageViews). */
export const REVIEW_REPORT_PATH = '/review';

// #region review-stops
export const REVIEW_STOPS: readonly ReviewStop[] = [
  {
    key: 'first-screen',
    title: 'The first screen',
    address: '/',
    minutes: 1,
    shows:
      'The running system reporting on itself: the answer time, the Azure bill, the time since the last roll and the people who have visited, read live.',
    doThis: 'Watch the four readouts fill in. Each one links to the page that explains it.',
    record: 'adr-one-reading-for-the-first-screen',
  },
  {
    key: 'room',
    title: 'Bid in the room',
    address: '/?view=inventory',
    minutes: 2,
    shows:
      '100,000 vehicles filtered, sorted and paged by the store, the auction rules on the server, and a room that bids back.',
    doThis:
      'Open any live lot and watch the room bid against itself. Sign in and place the minimum bid if you have two more minutes; the room answers within twenty seconds and your bid survives a reload.',
    record: 'adr-the-store-does-the-work',
  },
  {
    key: 'spend',
    title: 'What Azure charges',
    address: '/?view=admin&card=spend',
    minutes: 1,
    shows:
      "The bill by resource and the month's forecast, read from Azure once an hour and shown in public. The whole platform runs on one small plan.",
    doThis: 'Open the cost card. Every resource name links to its place in Azure.',
    record: 'adr-azure-costs',
  },
  {
    key: 'decision',
    title: 'One decision, with its code',
    address: '/?doc=adr-rules',
    minutes: 2,
    shows:
      'Seventeen standing rules, each written beside the test that holds it. The code on the page is read from the running build, not pasted in.',
    doThis: 'Scroll to the table; click any file name to land on it in GitHub.',
    record: 'adr-the-numbers-that-may-only-fall',
  },
  {
    key: 'gate',
    title: 'The gate that shipped this version',
    address: '/?view=admin&card=tests',
    minutes: 1,
    shows:
      'Every push through one gate: xUnit, Vitest, Playwright with axe, and the architecture rules, on both stores, then a blue-green roll with no downtime.',
    doThis:
      'Read the suites and their times for the build you are looking at. The link on the card opens the run on GitHub.',
    record: 'adr-five-minute-gate',
  },
  {
    key: 'stores',
    title: 'One codebase, two stores',
    address: '/?doc=sql-vs-cosmos',
    minutes: 2,
    shows:
      'The same build on Azure SQL Database and on Azure Cosmos DB, side by side, with the proof run that shows the same performance from both.',
    doThis:
      'Read the first table, then press the store toggle at the top of the page and watch the readouts switch.',
    record: 'adr-measuring-stores',
  },
  {
    key: 'resume',
    title: 'The resume',
    address: '/resume?pdf=1',
    minutes: 1,
    shows: 'Two pages. The six stops above are the evidence for what it claims.',
    doThis: 'Open the PDF. The top line is the title the site was built to prove.',
    record: 'adr-the-resume-has-an-address',
  },
];
// #endregion review-stops

/** The minutes the route has left from a stop (0 is the first), never below none or above the whole. */
export function minutesLeft(fromIndex: number): number {
  return REVIEW_STOPS.slice(Math.max(0, fromIndex)).reduce((sum, stop) => sum + stop.minutes, 0);
}
