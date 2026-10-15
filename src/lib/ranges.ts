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
  /** Where the lines come from: a record, a measurement or Steve's own number. */
  source: string;
};

// #region ranges
/** Steve's monthly budget for the whole subscription: over it the cost reads red ("Red at this is over 50$", in his words). */
export const MONTHLY_BUDGET_DOLLARS = 50;

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
    name: 'The month, forecast',
    unit: '$',
    amber: MONTHLY_BUDGET_DOLLARS * 0.8,
    red: MONTHLY_BUDGET_DOLLARS,
    source:
      "Steve's budget: red over $50 a month; amber from $40, or when Azure forecasts more than the month's own pace",
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

/**
 * The month's cost judged against the budget: red when the forecast, or what
 * has been spent already, passes the budget; amber from 80% of it, or when
 * Azure forecasts more than the month's own pace so far would reach (the bill
 * is speeding up); good otherwise. Null when there is nothing to judge.
 */
export function costTone(
  monthToDate: number,
  forecast: number | null,
  dayOfMonth: number,
  daysInMonth: number
): RangeTone {
  const expected = forecast ?? monthToDate;
  const worst = Math.max(expected, monthToDate);
  const pace = dayOfMonth <= 0 ? monthToDate : (monthToDate / dayOfMonth) * daysInMonth;
  const tone = toneOf('cost', worst);
  if (tone !== 'good') return tone;
  return forecast !== null && forecast > pace * 1.1 ? 'warn' : 'good';
}
