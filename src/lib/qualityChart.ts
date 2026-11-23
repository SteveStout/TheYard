/**
 * Does:      Places the Quality page's charts as its picture draws them (ADR-130, the addendum on the page as its
 *            picture): readings spaced by their day, round labelled gridlines, the version each number joined the
 *            ratchet, a dot at today, a dashed floor where the fence names one, and on the page's first chart the
 *            slip from the first reading to the ratchet with its three callouts.
 * Does not:  Draw anything (QualityChart.tsx does), read the history (the fence carries the readings) or import React.
 * Used by:   QualityChart.tsx, qualityChart.test.ts.
 */
import { figure, format, type QualityTrendData, type TrendPoint } from './qualityTrend';

// #region boxes
/** A drawing box, in viewBox units: the whole, and the margins the plot stands inside. */
export type ChartBox = {
  width: number;
  height: number;
  top: number;
  right: number;
  bottom: number;
  left: number;
};

/** The first chart at a desk, the picture's 960 by 280. */
export const SLIP_WIDE: ChartBox = {
  width: 960,
  height: 280,
  top: 26,
  right: 24,
  bottom: 40,
  left: 52,
};
/** The first chart on a phone, drawn at the picture's taller 350 by 300, never the wide one shrunk. */
export const SLIP_PHONE: ChartBox = {
  width: 350,
  height: 300,
  top: 34,
  right: 14,
  bottom: 38,
  left: 34,
};
/** One of the small multiples, and a coverage chart. */
export const SMALL: ChartBox = {
  width: 440,
  height: 150,
  top: 16,
  right: 14,
  bottom: 26,
  left: 40,
};
// #endregion boxes

const DAY_MS = 86_400_000;
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const WORDS = [
  'ZERO',
  'ONE',
  'TWO',
  'THREE',
  'FOUR',
  'FIVE',
  'SIX',
  'SEVEN',
  'EIGHT',
  'NINE',
  'TEN',
];

/** A reading's day as a count of days, so two readings on one day share an x. */
const dayOf = (date: string) => Math.round(Date.parse(`${date}T00:00:00Z`) / DAY_MS);

/** A day as the page writes one: "Oct 29". */
export function shortDay(date: string): string {
  const [, month, day] = date.split('-').map(Number);
  return `${MONTHS[(month ?? 1) - 1]} ${day}`;
}

/** The smallest round number at or over `value`: 1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6 or 8 of its power of ten. */
export function roundUp(value: number): number {
  if (value <= 0) return 1;
  const scale = 10 ** Math.floor(Math.log10(value));
  for (const step of [1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10]) {
    if (step * scale >= value - 1e-9) return Number((step * scale).toPrecision(6));
  }
  return 10 * scale;
}

/** The floor a fence names in its direction ("floor 90"), or null. */
export function floorOf(direction: string): number | null {
  const named = /\bfloor (\d+(?:\.\d+)?)\b/.exec(direction);
  return named ? Number(named[1]) : null;
}

/** The worst reading: the highest of a number that may only fall, the lowest of one that may only rise. */
function worstIndex(data: QualityTrendData): number {
  const rises = /rise/.test(data.direction);
  return data.points.reduce(
    (worst, point, index) =>
      (rises ? point.value < data.points[worst].value : point.value > data.points[worst].value)
        ? index
        : worst,
    0
  );
}

/**
 * The vertical scale. A share with a floor stands between a little under its lowest reading or floor and a little
 * over its highest, three lines; every other number starts at zero and tops out at a round number over its highest,
 * three lines on a small chart and, on the first chart, a line every 1, 2 or 5 of a power of ten, the finest step
 * that draws no more than three (0, 50 and 100 under a top of 120).
 */
export function scaleOf(data: QualityTrendData): { low: number; high: number; ticks: number[] } {
  const values = data.points.map((p) => p.value);
  const top = values.length ? Math.max(...values) : 1;
  const floor = floorOf(data.direction);
  if (floor !== null) {
    const low = Math.floor(Math.min(...values, floor) - 5);
    const high = Math.min(100, Math.ceil(top + 3));
    return { low, high, ticks: [low, Math.round((low + high) / 2), high] };
  }
  const high = roundUp(top * 1.08);
  if (data.form !== 'slip') return { low: 0, high, ticks: [0, high / 2, high] };
  const step = [1, 2, 5, 10, 20, 50]
    .map((unit) => unit * 10 ** Math.floor(Math.log10(high / 10)))
    .find((unit) => Math.floor(high / unit) + 1 <= 3)!;
  const ticks = Array.from({ length: Math.floor(high / step) + 1 }, (_, i) =>
    Number((i * step).toPrecision(6))
  );
  return { low: 0, high, ticks };
}

/** A path through points already placed. */
const path = (points: { x: number; y: number }[]) =>
  points.map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x} ${p.y}`).join(' ');

const round = (value: number) => Math.round(value * 10) / 10;

/** One callout: the point, the end of its leader, the value in bold and the line under it. */
export type Callout = {
  x: number;
  y: number;
  tx: number;
  ty: number;
  anchor: 'start' | 'end';
  value: string;
  words: string;
};

/** Everything a Quality chart draws, placed in its box. */
export function chartGeometry(data: QualityTrendData, box: ChartBox, phone = false) {
  const { low, high, ticks } = scaleOf(data);
  const wide = box.width - box.left - box.right;
  const tall = box.height - box.top - box.bottom;
  const days = data.points.map((p) => dayOf(p.date));
  const first = days[0] ?? 0;
  const span = Math.max(1, (days[days.length - 1] ?? 0) - first);
  const xOf = (day: number) => round(box.left + (wide * (day - first)) / span);
  const yOf = (value: number) => round(box.top + tall * (1 - (value - low) / (high - low || 1)));
  const at = data.points.map((p: TrendPoint, i) => ({ ...p, x: xOf(days[i]), y: yOf(p.value) }));
  const lastPre = at.reduce((last, p, i) => (p.preScan ? i : last), -1);
  const base = yOf(low);
  const joined = at.find((p) => p.version !== '' && p.version === data.marker) ?? null;
  const end = at[at.length - 1] ?? null;
  const floor = floorOf(data.direction);
  const measured = at.slice(lastPre + 1);
  const label = (p: TrendPoint) =>
    `${data.label}: ${figure(p.value, data.label)} on ${shortDay(p.date)}`;
  const callouts: Callout[] = [];
  if (data.form === 'slip' && at.length > 1 && end) {
    const worst = at[worstIndex(data)];
    const start = at[0];
    callouts.push(
      {
        x: start.x,
        y: start.y,
        tx: start.x + 24,
        ty: start.y + 40,
        anchor: 'start',
        value: figure(start.value, data.label),
        words: shortDay(start.date),
      },
      {
        x: worst.x,
        y: worst.y,
        tx: worst.x + (phone ? 22 : 30),
        ty: worst.y - 14,
        anchor: 'start',
        value: figure(worst.value, data.label),
        words: phone ? 'worst' : `the worst, ${shortDay(worst.date)}`,
      },
      {
        x: end.x,
        y: end.y,
        tx: end.x - 30,
        ty: end.y - 40,
        anchor: 'end',
        value: figure(end.value, data.label),
        words: 'today',
      }
    );
  }
  const slipWords =
    joined && at[0]
      ? phone
        ? 'THE SLIP'
        : `THE SLIP, ${slipSpan(dayOf(joined.date) - dayOf(at[0].date))}`
      : '';
  return {
    box,
    plot: { left: box.left, right: box.width - box.right, top: box.top, bottom: base },
    line: path(measured),
    preScan: lastPre >= 0 ? path(at.slice(0, lastPre + 2)) : '',
    area:
      data.form === 'slip' && at.length > 1
        ? `${path(at)} L${end!.x} ${base} L${at[0].x} ${base} Z`
        : '',
    grid: ticks.map((value) => ({ y: yOf(value), text: tickText(value, high) })),
    xLabels: xLabels(at, joined, data.form ?? ''),
    // The ratchet's words: its day at a desk, set at the top; on a phone the name alone, set lower to clear the worst.
    join: joined
      ? {
          x: joined.x,
          y: phone ? round(box.top + tall * 0.45) : box.top + 6,
          words: phone ? 'THE RATCHET' : `THE RATCHET, ${shortDay(joined.date).toUpperCase()}`,
        }
      : null,
    slip: data.form === 'slip' && joined ? { right: joined.x, words: slipWords } : null,
    end: end ? { x: end.x, y: end.y } : null,
    floor: floor !== null ? { y: yOf(floor), words: `FLOOR ${format(floor)}` } : null,
    callouts,
    summary: at.length
      ? `${label(at[0])}, ${label(at[worstIndex(data)])} at its worst, ${label(end!)} today`
      : 'No readings in this build',
  };
}

/** The slip's length in words: "TWO WEEKS" for whole weeks, else "NINE DAYS"; past ten, the figure. */
export function slipSpan(days: number): string {
  const say = (count: number) => WORDS[count] ?? String(count);
  if (days > 0 && days % 7 === 0) return `${say(days / 7)} WEEK${days === 7 ? '' : 'S'}`;
  return `${say(days)} DAY${days === 1 ? '' : 'S'}`;
}

/** A gridline's number: whole when the scale is whole, two places when it is a ratio. */
function tickText(value: number, high: number): string {
  return high < 5 ? value.toFixed(2) : String(Math.round(value));
}

/** The days along the foot: the first and today, and on the first chart the ratchet's day between them. */
function xLabels(
  at: { x: number; date: string }[],
  joined: { x: number; date: string } | null,
  form: string
) {
  const first = at[0];
  const last = at[at.length - 1];
  if (!first || !last) return [];
  return [
    { x: first.x, text: shortDay(first.date), anchor: 'start' as const },
    ...(form === 'slip' && joined
      ? [{ x: joined.x, text: shortDay(joined.date), anchor: 'middle' as const }]
      : []),
    { x: last.x, text: shortDay(last.date), anchor: 'end' as const },
  ];
}
