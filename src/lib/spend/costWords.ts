/**
 * The cost card's words: which cost window a chart window stands for, money as
 * the bill writes it, days and months in words, and the card's headline. It is
 * its own file because every picture on the card labels itself with these, so
 * they must say a day or an amount the same way everywhere.
 */
import type { MachineWindow } from '../machineChart';
import type { CostReport, CostWindow } from './costReport';

// #region spend-words
/**
 * The cost window a chart window stands for. Azure bills by the day, so the
 * hour every other chart offers is shown as the last day, and the card says so
 * rather than pretending to an hour it cannot have.
 */
export function costWindowFor(window: MachineWindow): CostWindow {
  return window === '1h' ? '24h' : window;
}

/** Money as the bill writes it: dollars and cents, and a charge under a cent said as that. */
export function money(value: number, currency = 'USD'): string {
  if (value > 0 && value < 0.005) return `under ${money(0.01, currency)}`;
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency,
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);
}

/** The months in order, for turning a yyyy-MM-dd into words. */
const MONTHS = [
  'January',
  'February',
  'March',
  'April',
  'May',
  'June',
  'July',
  'August',
  'September',
  'October',
  'November',
  'December',
];

/** A day as a person reads it: "30 September". */
export function dayWords(day: string): string {
  const [, month, date] = day.split('-').map(Number);
  return `${date} ${MONTHS[month - 1] ?? ''}`.trim();
}

/** A day as an axis reads it: "30 Sep". */
export function dayShort(day: string): string {
  const [, month, date] = day.split('-').map(Number);
  return `${date} ${(MONTHS[month - 1] ?? '').slice(0, 3)}`.trim();
}

/** The month a yyyy-MM names, in words. */
export function monthWords(month: string): string {
  return MONTHS[Number(month.split('-')[1]) - 1] ?? month;
}

/**
 * The card's two headline sentences: what the month has cost, and what Azure
 * says it will cost by its end. A month with no forecast says so, and a newest
 * day Azure is still adding to is named, because a total that is still moving
 * should look like one.
 */
export function headline(report: CostReport): string[] {
  if (!report.available || report.month === null) return [];
  const lines = [
    `${monthWords(report.month)} so far: ${money(report.month_to_date, report.currency)}.`,
  ];
  lines.push(
    report.forecast_month === null
      ? 'Azure has not forecast the rest of the month yet.'
      : `Azure forecasts ${money(report.forecast_month, report.currency)} by the end of ${monthWords(report.month)}.`
  );
  if (report.newest_partial && report.newest_day !== null) {
    lines.push(
      `${dayWords(report.newest_day)} is still being added to: Azure reports a day eight to twenty four hours late.`
    );
  }
  return lines;
}
// #endregion spend-words
