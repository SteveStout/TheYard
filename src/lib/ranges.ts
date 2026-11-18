/**
 * The normal range of every number the Admin tab judges, in one table: where
 * it turns amber (worth a look) and where it turns red (someone should be
 * looking), in the number's own unit, and why the line sits where it does. The
 * tiles, the Overview and the cards all read this table, so a number cannot be
 * amber on the strip and fine on its card.
 *
 * No React in here: plain data and two plain functions, tested on their own.
 */

/** How a judged number reads: inside its range, past amber, or past red. */
export type RangeTone = 'good' | 'warn' | 'bad';

export type Range = {
  /** What is measured, as a person would name it. */
  name: string;
  /** The unit the two lines are in, as written after a number. */
  unit: string;
  /** At or past this the number is amber. */
  amber: number;
  /** At or past this the number is red. */
  red: number;
  /** True when a smaller number is the worse one (days left on a certificate). */
  lowIsBad?: boolean;
  /** Where the lines come from: a record, a measurement or the owner's own number. */
  source: string;
};

// #region ranges
/**
 * The owner's monthly budget for the whole subscription: over it the cost reads red. The card grades against the
 * amount Azure's own budget holds when the cost read carries it, and against this number only when it does not.
 */
export const MONTHLY_BUDGET_DOLLARS = 70;

/** Ten dollars under the budget, the amber line the owner named, rather than a share of it. */
export const BUDGET_AMBER_DOLLARS = MONTHLY_BUDGET_DOLLARS - 10;

/** A day costs this many times the week's median before it reads as a spike. */
export const SPIKE_MULTIPLE = 3;

/** And never less than this, so a cheap week does not flag an ordinary day. */
export const SPIKE_FLOOR_DOLLARS = 5;

/** Every judged number, by key. A share is written from 0 to 1 and said as a percentage. */
export const RANGES = {
  memory: {
    name: 'Memory of its limit',
    unit: '%',
    amber: 0.8,
    red: 0.95,
    source: 'the container is restarted by the platform at its limit; 80% leaves room for a burst',
  },
  p95: {
    name: 'Ninety-fifth answer time',
    unit: 'ms',
    amber: 1_000,
    red: 3_000,
    source: 'ADR-080: a second is slow to a visitor, three seconds is broken',
  },
  coldStart: {
    name: 'Cold start, ready',
    unit: 'ms',
    amber: 30_000,
    red: 60_000,
    source:
      'measured at 26,677 ms on the live site; past a minute the platform warm-up probe gives up',
  },
  ruMinute: {
    name: 'Busiest minute of the free request units',
    unit: '%',
    amber: 0.5,
    red: 0.9,
    source: 'the free tier carries 1,000 RU a second; past 90% a burst is billed or throttled',
  },
  sqlDtu: {
    name: 'Azure SQL busiest share',
    unit: '%',
    amber: 0.6,
    red: 0.9,
    source: 'the serverless tier scales at its ceiling; 60% is the early look',
  },
  keptWarm: {
    name: 'Slowest kept-warm read',
    unit: 'ms',
    amber: 1_000,
    red: 3_000,
    source: 'the same lines as an answer a visitor waits for',
  },
  cost: {
    name: "The month, at yesterday's rate",
    unit: '$',
    amber: BUDGET_AMBER_DOLLARS,
    red: MONTHLY_BUDGET_DOLLARS,
    source:
      "Steve's budget: $60 on 11/4 (\"adjust it up 10$ then as we've added more resources\"), $70 on 11/11, read from Azure's budget when the cost read carries it; red at the budget, amber $10 under it, graded on what is being spent, never on Azure's forecast",
  },
  clientErrorsHour: {
    name: 'Turned away (4xx) in the hour',
    unit: '',
    amber: 50,
    red: 200,
    source:
      'a quiet hour turns away a handful of scanners; 50 is a crawl, 200 is an attack or a broken link',
  },
  gateSeconds: {
    name: 'The gate, start to verdict',
    unit: 's',
    amber: 300,
    red: 600,
    source: 'ADR-068: every check in under five minutes',
  },
  liveSeconds: {
    name: 'Push to live',
    unit: 's',
    amber: 900,
    red: 1800,
    source:
      'GATEFLOW1117: push to live read 929 s at 1.0.5.75, and R1 aims at a version every 16 to 20 minutes, so a quarter of an hour is the target and half an hour the red line',
  },
  certificateDays: {
    name: 'Days left on the certificate',
    unit: 'days',
    amber: 30,
    red: 7,
    lowIsBad: true,
    source: 'the platform renews at 30 days; under a week a renewal has failed',
  },
  sqlStorage: {
    name: 'Azure SQL storage of its tier',
    unit: '%',
    amber: 0.8,
    red: 0.95,
    source: 'the tier holds 2 GB and refuses writes when full',
  },
} satisfies Record<string, Range>;
// #endregion ranges

export type RangeKey = keyof typeof RANGES;

/** Where a number sits in its range. A number not read yet is not judged here; the caller says so. */
export function toneOf(key: RangeKey, value: number): RangeTone {
  const range: Range = RANGES[key];
  if (range.lowIsBad === true) {
    return value <= range.red ? 'bad' : value <= range.amber ? 'warn' : 'good';
  }
  return value >= range.red ? 'bad' : value >= range.amber ? 'warn' : 'good';
}

/** One line of a range in the number's unit, "amber at 80%, red at 95%", for under the number. */
export function rangeWords(key: RangeKey): string {
  const range: Range = RANGES[key];
  const say = (n: number) => {
    if (range.unit === '%') return `${Math.round(n * 100)}%`;
    if (range.unit === '$') return `$${n.toLocaleString('en-US')}`;
    if (range.unit === 'ms' && n >= 1_000) return `${n / 1_000} s`;
    return `${n.toLocaleString('en-US')}${range.unit === '' ? '' : ` ${range.unit}`}`;
  };
  const way = range.lowIsBad === true ? 'under' : 'at';
  return `amber ${way} ${say(range.amber)}, red ${way} ${say(range.red)}`;
}

/** One day as the spike rule reads it: what it cost and the median of the finished week before it. */
export type SpikeReading = { cost: number; median: number | null };

/** The line a day must pass to be a spike: three times the week's median, and never under the floor. */
export function spikeLine(median: number | null): number {
  return Math.max(SPIKE_MULTIPLE * (median ?? 0), SPIKE_FLOOR_DOLLARS);
}

/** A day over its spike line is red; every other day is fine. */
export function spikeTone(day: SpikeReading): RangeTone {
  return day.cost > spikeLine(day.median) ? 'bad' : 'good';
}

/**
 * The month's cost judged on what is being spent: a spike on the newest two days (the last finished
 * day and the one Azure is still adding to) is red whatever the month reads; otherwise the budget's
 * lines over the larger of the month so far and the month at yesterday's rate. Azure's forecast is
 * not an input: it replays old incidents into days that have not happened.
 */
export function costTone(
  monthToDate: number,
  monthAtYesterday: number | null | undefined,
  spikeDays: readonly SpikeReading[] = [],
  budget: number = MONTHLY_BUDGET_DOLLARS
): RangeTone {
  if (spikeDays.slice(0, 2).some((day) => spikeTone(day) === 'bad')) return 'bad';
  const month = Math.max(monthToDate, monthAtYesterday ?? monthToDate);
  return month >= budget ? 'bad' : month >= budget - 10 ? 'warn' : 'good';
}

/** The cost range in words against <budget>: amber $10 under it, red at it. */
export function costRangeWords(budget: number = MONTHLY_BUDGET_DOLLARS): string {
  return `amber at $${budget - 10}, red at $${budget}`;
}
