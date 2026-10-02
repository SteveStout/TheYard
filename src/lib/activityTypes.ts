// The shapes the activity endpoints send, and the fixed lists of names the
// card shows for them. Every other activity module reads these, so they sit in
// one file of their own with no arithmetic in it, and a change to the wire
// shows up in one place (ADR: Site activity, and the line an address does not cross).

/** The windows the card can show: the last day, the last week, the last month. */
export type ActivityWindow = '24h' | '7d' | '30d';

/** The windows in the order the card's buttons list them. */
export const ACTIVITY_WINDOWS: readonly ActivityWindow[] = ['24h', '7d', '30d'];

/** One point on a line: when it was, how many requests, and how many of those were bots. */
export type ActivityPoint = { at: string; requests: number; bots: number };

/** One line on the chart: a store's points, or everybody's under the key `all`. */
export type ActivitySeries = { store: string; name: string; points: ActivityPoint[] };

/** Whether a store could be read for the report, and why not when it could not. */
export type ActivityStoreState = {
  store: string;
  name: string;
  available: boolean;
  reason: string;
};

/** One address on the site and how many requests asked for it. */
export type ActivityPath = { path: string; requests: number };

/**
 * Who a visitor-day was: people, scanners and crawlers, and the site's own
 * reads (its tools under the self mark, and App Service asking after the
 * container from the loopback address). The three add up to the day's
 * visitor-days.
 */
export type ActivityKinds = { people: number; scanners: number; self: number };

/** One UTC day: its visitor-days by kind, its people and bots, and the same counts per store. */
export type ActivityDay = ActivityKinds & {
  day: string;
  visitors: number;
  humans: number;
  bots: number;
  by_store: ({ store: string; visitors: number } & ActivityKinds)[];
};

/** The path to the resume: the four steps the site exists for, in the order they are walked. */
export type ActivityStep = 'site' | 'inventory' | 'author' | 'resume';

/** Each step of the path to the resume as the card names it. */
export const STEP_NAMES: Readonly<Record<ActivityStep, string>> = {
  site: 'Opened the site',
  inventory: 'The inventory',
  author: 'About Steven',
  resume: 'The resume',
};

/** One kind of traffic over the window: visitor-days summed over the days, requests, what it asked for, per store. */
export type ActivityWhoEntry = {
  visitor_days: number;
  requests: number;
  top_paths: ActivityPath[];
  path: { step: ActivityStep; visitor_days: number }[];
  sources: { host: string; visitor_days: number }[];
  by_store: { store: string; visitor_days: number; requests: number }[];
};

/** The card's toggle: visitors only (the people) or all traffic (the three kinds together). */
export type ActivityWho = 'people' | 'all';

/** The toggle's two settings, in the order the card shows them. */
export const ACTIVITY_WHO: readonly ActivityWho[] = ['people', 'all'];

/**
 * The whole report for one window: the totals, the lines, the days, each kind
 * of traffic, the stores, and how the collector and the keeper are doing.
 */
export type ActivityReport = {
  window: ActivityWindow;
  /** Whether this site serves the per-visitor rows at all (off by default). */
  visitor_rows: boolean;
  bucket: string;
  since: string;
  until: string;
  totals: { requests: number; bots: number; humans: number };
  by_store: { store: string; requests: number; bots: number; humans: number }[];
  series: ActivitySeries[];
  days: ActivityDay[];
  who: {
    people: ActivityWhoEntry;
    scanners: ActivityWhoEntry;
    self: ActivityWhoEntry;
    all: ActivityWhoEntry;
  };
  top_paths: ActivityPath[];
  stores: ActivityStoreState[];
  /** The key of the one store every batch is written to. */
  kept_by: string;
  /** The keeper's own sentence for how long rows are kept, when it is up ("kept in Azure Cosmos DB with no expiry"). */
  retention: string | null;
  collector: {
    offered: number;
    written: number;
    failed_batches: number;
    /** Hits a full channel dropped, oldest first, since the process started. */
    dropped: number;
    last_write: string | null;
    interval_seconds: number;
  };
  /** What keeping activity has cost the keeper since the process started; null on a store with no unit for it. */
  cost: { request_units: number; operations: number; failures: number } | null;
};

/** One visitor on one UTC day: who it was, its network, its store, its first and last hit, and what it asked for. */
export type ActivityVisitor = {
  visitor: string;
  network: string;
  store: string;
  day: string;
  first_seen: string;
  last_seen: string;
  requests: number;
  bots: number;
  top_paths: ActivityPath[];
};

/** The visitor table's rows for one window, when the site serves them. */
export type ActivityVisitors = {
  window: ActivityWindow;
  since: string;
  until: string;
  count: number;
  visitors: ActivityVisitor[];
};
