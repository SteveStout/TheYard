/**
 * The bars by type of resource, and the words that sit under the resource and
 * type pictures. It is its own file because the bars and those sentences share
 * one rule: every figure there is a month at the window's rate, and saying so
 * belongs beside the code that draws it.
 */
import type { CostKind, CostReport } from './costReport';
import { monthWords } from './costWords';

// #region spend-bars
/**
 * Each type's bar as what it comes to in a month, a share of the costliest, so
 * the longest bar is where the money goes and a type the bill carries at $0.00
 * draws no bar. The server works out the month; this only lays it out. The
 * count rides beside it in words: a bar measures one thing.
 */
export function typeBars(
  types: CostKind[]
): { label: string; resources: number; monthly: number; share: number }[] {
  const most = Math.max(0, ...types.map((kind) => kind.monthly));
  return types.map((kind) => ({
    label: kind.label,
    resources: kind.resources,
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
  const days = report.rate_days === 1 ? 'day' : `${report.rate_days} days`;
  const month = report.month === null ? 'the month' : monthWords(report.month);
  return `Each figure is a month at the rate of the last finished ${days}, over the ${report.month_days} days of ${month}.`;
}

/** How many of a type are on the bill, in words. */
export function resourcesWords(count: number): string {
  return `${count} resource${count === 1 ? '' : 's'}`;
}
// #endregion spend-bars
