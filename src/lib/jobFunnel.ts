/**
 * The job hunt's funnel card (ADR: The Nightly Function on the Admin tab, the addendum on the funnel): the
 * report GET /api/admin/funnel answers from the Function's daily line, the three windows, and the words the
 * card prints. The server adds the days and grades the week; this file only says it. No React in here.
 */

/** The windows the card offers, in the order its pills show them. */
export const FUNNEL_WINDOWS = ['24h', '7d', '30d'] as const;

/** One of the three windows. */
export type FunnelWindow = (typeof FUNNEL_WINDOWS)[number];

/** The six counts in the order the line writes them and the API answers them. */
export const FUNNEL_STEPS = [
  'Postings scanned',
  'READY',
  'Applied',
  'People reached',
  'Replies from people',
  'Calls booked',
] as const;

/** The card's report, as GET /api/admin/funnel answers it. */
export type JobFunnel = {
  window: string;
  available: boolean;
  note: string | null;
  /** Six counts over the window, in FUNNEL_STEPS' order. */
  counts: number[];
  days: number;
  week_booked: number;
  week_target: number;
  newest_ms: number | null;
  tone: 'fine' | 'amber' | 'red' | 'unread';
  source: string;
};

/** One step of the funnel with its count and its share of the step before it, null for the first or after a zero. */
export type FunnelStep = { name: string; count: number; ofBefore: number | null };

/** The six steps, each with the share of the step before it that went on to it. */
export function funnelSteps(funnel: Pick<JobFunnel, 'counts'>): FunnelStep[] {
  return FUNNEL_STEPS.map((name, at) => {
    const count = Math.max(0, funnel.counts[at] ?? 0);
    const before = at === 0 ? 0 : Math.max(0, funnel.counts[at - 1] ?? 0);
    return {
      name,
      count,
      ofBefore: at === 0 || before === 0 ? null : Math.round((count / before) * 100),
    };
  });
}

/** The week's line under the steps: "1 call booked in the last 7 days, against 2 to 3 a week". */
export function weekLine(funnel: Pick<JobFunnel, 'week_booked' | 'week_target'>): string {
  const calls = funnel.week_booked === 1 ? '1 call' : `${funnel.week_booked} calls`;
  return `${calls} booked in the last 7 days, against ${funnel.week_target} to ${funnel.week_target + 1} a week`;
}

/** How many days the window's numbers come from: "from 6 days of lines", or none. */
export function daysLine(days: number): string {
  return days === 0
    ? 'no funnel line in this window'
    : days === 1
      ? 'from 1 day of lines'
      : `from ${days} days of lines`;
}
