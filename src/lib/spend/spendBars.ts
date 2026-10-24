/**
 * The bars by type of resource, and the words that sit under the resource and
 * type pictures. It is its own file because the bars and those sentences share
 * one rule: every figure there is a month at the window's rate, and saying so
 * belongs beside the code that draws it.
 */
import type { CostKind, CostReport } from './costReport';
import { money, monthWords } from './costWords';

// #region spend-bars
/**
 * Each type's bar as what it comes to in a month, a share of the costliest, so
 * the longest bar is where the money goes and a type the bill carries at $0.00
 * draws no bar. The server works out the month; this only lays it out. The
 * count rides beside it in words: a bar measures one thing.
 */
export function typeBars(
  types: CostKind[]
): { label: string; resources: number; cost: number; monthly: number; share: number }[] {
  const most = Math.max(0, ...types.map((kind) => kind.monthly));
  return types.map((kind) => ({
    label: kind.label,
    resources: kind.resources,
    cost: kind.cost,
    monthly: kind.monthly,
    share: most === 0 ? 0 : kind.monthly / most,
  }));
}

/**
 * The sentence under the resource and type pictures: every figure there is a
 * month at the rate of the window's finished days, so a day's cents are never
 * read as a month's bill.
 */
export function rateWords(report: CostReport): string {
  if (report.window === '24h') {
    return 'Each figure is what yesterday and today have cost so far, never scaled to a month: one day is too short to call a rate.';
  }
  const days = report.rate_days === 1 ? 'day' : `${report.rate_days} days`;
  const month = report.month === null ? 'the month' : monthWords(report.month);
  return `Each figure is a month at the rate of the last finished ${days}, over the ${report.month_days} days of ${month}.`;
}

/**
 * One resource's or type's figure in the window's words: on the day window, what it has cost so far,
 * because a one-day spike is never shown as a month; on the week and the month, what it comes to in a
 * month and the window that rate is taken from.
 */
export function monthReading(
  report: CostReport,
  figure: { cost: number; monthly: number }
): string {
  if (report.window === '24h')
    return `${money(figure.cost, report.currency)} yesterday and today so far`;
  const days = report.window === '7d' ? '7' : '30';
  return `${money(figure.monthly, report.currency)} a month at the last ${days} days' rate`;
}

/** How many of a type are on the bill, in words. */
export function resourcesWords(count: number): string {
  return `${count} resource${count === 1 ? '' : 's'}`;
}
// #endregion spend-bars
