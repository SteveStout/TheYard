/**
 * The Overview card's four rows, the five-second answer to the tab's four
 * questions (ADR: The Admin tab, as a product, the addendum on the Overview):
 * is it up, is it fast, is it costing anything, what broke. Each row is the
 * question, its number, the line under the number, its normal range and a
 * tone. The rows are built from what the strip already holds plus the page
 * sweep and the cost answer, so the Overview and the tiles cannot disagree.
 *
 * No React in here: one plain function over plain shapes, tested on its own.
 */
import { clockLabel } from './machineChart';
import { costTone, MONTHLY_BUDGET_DOLLARS, rangeWords } from './ranges';
import { money, monthWords } from './spend';
import type { StatTile, TileTone } from './statTiles';
import { uptimeWords } from './statTiles';

/** One row of the Overview. */
export type OverviewRow = {
  key: 'up' | 'fast' | 'cost' | 'broke';
  question: string;
  value: string;
  detail: string;
  /** The normal range, said under the number. */
  range: string;
  tone: TileTone;
};

/** What the Overview is built from; every field is null until it has been read. */
export type OverviewReadings = {
  health: {
    status: string;
    uptime_seconds: number;
    checks: { status: string }[];
    kept_warm?: { failed: number; reads: number } | null;
  } | null;
  /** The last page sweep: when, what started it, the build, and the two counts. */
  pages: { at: string; trigger: string; version: string; checked: number; up: number } | null;
  cost: {
    available: boolean;
    month: string | null;
    /** The newest day the bill has, yyyy-MM-dd: the month's pace is measured to it. */
    newest_day: string | null;
    month_days: number;
    month_to_date: number;
    forecast_month: number | null;
    currency: string;
    /** Why there is no bill, in the cost reader's words, such as a throttled read or a missing role. */
    note?: string | null;
  } | null;
  /** The strip's tiles, for the speed and the errors rows. */
  tiles: StatTile[];
  /** When this process started, as the metrics read says (startup.started_at); null before it answers. */
  startedAt: string | null;
};

// #region overview-rows
/** The four rows, in the order somebody asks the questions. */
export function overviewRows(readings: OverviewReadings): OverviewRow[] {
  return [upRow(readings), fastRow(readings), costRow(readings), brokeRow(readings)];
}

/** Up: every check and every page, and when the process last started and the site last rolled. */
function upRow({ health, pages, startedAt }: OverviewReadings): OverviewRow {
  const question = 'Is it up?';
  const range = 'red on one failing check or one page down';
  if (health === null) {
    return { key: 'up', question, value: '…', detail: 'not read yet', range, tone: 'waiting' };
  }
  const passing = health.checks.filter((check) => check.status === 'pass').length;
  const checksFine = passing === health.checks.length;
  const pagesFine = pages === null || pages.up === pages.checked;
  const restart =
    (startedAt === null ? 'restarted' : `restarted ${dayClock(startedAt)}`) +
    `, ${uptimeWords(health.uptime_seconds)} ago`;
  const rolled =
    pages === null
      ? ''
      : pages.trigger === 'roll'
        ? `; rolled ${dayClock(pages.at)} at v${pages.version}`
        : `; pages checked ${dayClock(pages.at)}`;
  return {
    key: 'up',
    question,
    value:
      `${passing} of ${health.checks.length} checks` +
      (pages === null ? '' : `, ${pages.up} of ${pages.checked} pages`),
    detail: restart + rolled,
    range,
    tone: checksFine && pagesFine ? 'good' : 'bad',
  };
}

/** Fast: the speed tile's reading, with the ninety-fifth's range under it. */
function fastRow({ tiles }: OverviewReadings): OverviewRow {
  const tile = tiles.find((entry) => entry.key === 'speed');
  return {
    key: 'fast',
    question: 'Is it fast?',
    value: tile?.value ?? '…',
    detail: tile?.detail ?? 'not read yet',
    range: `the ninety-fifth: ${rangeWords('p95')}`,
    tone: tile?.tone ?? 'waiting',
  };
}

/** Cost: the month so far against Steve's budget, and what Azure forecasts. */
function costRow({ cost }: OverviewReadings): OverviewRow {
  const question = 'Is it costing anything?';
  const range = `the month against the $${MONTHLY_BUDGET_DOLLARS} budget: ${rangeWords('cost')}`;
  if (cost === null) {
    return { key: 'cost', question, value: '…', detail: 'not read yet', range, tone: 'waiting' };
  }
  if (!cost.available || cost.month === null) {
    return {
      key: 'cost',
      question,
      value: 'no bill to show',
      // The reader's own reason, so a throttled read does not say this site never reads the bill.
      detail: cost.note ?? 'this site does not read the bill',
      range,
      tone: 'plain',
    };
  }
  // The pace runs to the newest day on the bill, not to today: Azure reports a day late.
  const day = cost.newest_day === null ? 0 : Number(cost.newest_day.slice(8, 10));
  const days = cost.month_days;
  return {
    key: 'cost',
    question,
    value: money(cost.month_to_date, cost.currency),
    detail:
      `${monthWords(cost.month)} so far` +
      (cost.forecast_month === null
        ? '; no forecast yet'
        : `; Azure forecasts ${money(cost.forecast_month, cost.currency)}`),
    range,
    tone: costTone(cost.month_to_date, cost.forecast_month, day, days),
  };
}

/** Broke: the errors tile, and a kept-warm pass that failed. */
function brokeRow({ tiles, health }: OverviewReadings): OverviewRow {
  const tile = tiles.find((entry) => entry.key === 'errors');
  const failedWarm = health?.kept_warm?.failed ?? 0;
  const tone: TileTone = failedWarm > 0 ? 'bad' : (tile?.tone ?? 'waiting');
  return {
    key: 'broke',
    question: 'What broke?',
    value: tile?.value ?? '…',
    detail:
      (tile?.detail ?? 'not read yet') +
      (failedWarm > 0 ? `; ${failedWarm} kept-warm reads failed` : ''),
    range: 'red on any 5xx answer, listed error or failed kept-warm read',
    tone,
  };
}
/**
 * A time today as its clock, and a time on any other day with the day and month in front, so a
 * roll from last week does not read as this morning's.
 */
function dayClock(at: string): string {
  const when = new Date(at);
  if (Number.isNaN(when.getTime())) return '';
  if (when.toDateString() === new Date().toDateString()) return clockLabel(at);
  return `${when.getDate()} ${when.toLocaleString('en-US', { month: 'short' })} ${clockLabel(at)}`;
}
// #endregion overview-rows
