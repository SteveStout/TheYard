/**
 * What Azure charges, for the cost card on the Admin tab (ADR: What Azure
 * charges): the answer's shape, the window a chart window maps to, money in
 * words, and the geometry of the three pictures (the spend line with its
 * forecast, the donut by resource, the bars by type). The server owns every
 * figure; this file only lays out what the server said.
 *
 * No React in here: each is a plain function, so each is a rule a test can read.
 */
import type { MachineWindow } from './machineChart';

// #region spend-types
export type CostWindow = '24h' | '7d' | '30d';

export type CostPoint = { day: string; cost: number; total: number; partial: boolean };

export type CostSlice = { name: string; type: string; cost: number; share: number; count: number };

export type CostKind = { type: string; label: string; resources: number; cost: number };

/** GET /api/admin/costs, as the server writes it (api/TheYard.Api/CostReport.cs). */
export type CostReport = {
  window: CostWindow;
  windows: CostWindow[];
  available: boolean;
  note: string | null;
  currency: string;
  read_at_ms: number | null;
  newest_day: string | null;
  newest_partial: boolean;
  month: string | null;
  month_to_date: number;
  forecast_month: number | null;
  window_total: number;
  days: CostPoint[];
  forecast: CostPoint[];
  resources: CostSlice[];
  types: CostKind[];
};
// #endregion spend-types

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

// #region spend-line
export type LineBox = {
  width: number;
  height: number;
  left: number;
  right: number;
  top: number;
  bottom: number;
};

export const SPEND_BOX: LineBox = {
  width: 640,
  height: 220,
  left: 56,
  right: 16,
  top: 16,
  bottom: 28,
};

export type SpendLine = {
  /** The spend so far, a running total, as an SVG path. */
  actual: string;
  /** The forecast carried on from the last reported day, as an SVG path; empty when not drawn. */
  forecast: string;
  /** Where the newest day sits, for its marker; null when there are no days. */
  newest: { x: number; y: number; partial: boolean } | null;
  /** The axis: a few round amounts and where they sit. */
  ticks: { y: number; label: string }[];
  /** The first and last day under the line, and where each sits. */
  ends: { x: number; label: string }[];
  ceiling: number;
};

/** A round top for the axis: 1, 2 or 5 times a power of ten, at or above the highest reading. */
export function niceCeiling(value: number): number {
  if (value <= 0) return 1;
  const power = 10 ** Math.floor(Math.log10(value));
  for (const step of [1, 2, 5, 10]) {
    if (value <= step * power) return step * power;
  }
  return 10 * power;
}

/**
 * The spend line: each reported day's running total, and on the month window
 * the forecast carried on from it to the month's end. A day is a step on the
 * axis whether it cost anything or not; a day not yet reported is not drawn.
 */
export function spendLine(
  report: CostReport,
  withForecast: boolean,
  box: LineBox = SPEND_BOX
): SpendLine {
  const ahead = withForecast ? report.forecast : [];
  const all = [...report.days, ...ahead];
  const ceiling = niceCeiling(Math.max(0, ...all.map((point) => point.total)));
  const innerWidth = box.width - box.left - box.right;
  const innerHeight = box.height - box.top - box.bottom;
  const step = all.length <= 1 ? 0 : innerWidth / (all.length - 1);
  const x = (index: number) => box.left + (all.length <= 1 ? innerWidth / 2 : index * step);
  const y = (value: number) => box.top + innerHeight - (value / ceiling) * innerHeight;
  const path = (points: { x: number; y: number }[]) =>
    points
      .map(
        (point, index) => `${index === 0 ? 'M' : 'L'}${point.x.toFixed(1)},${point.y.toFixed(1)}`
      )
      .join(' ');

  const actualPoints = report.days.map((point, index) => ({ x: x(index), y: y(point.total) }));
  const last = report.days.length - 1;
  // The forecast starts where the reported line ends, so the two read as one line that turns to dashes.
  const forecastPoints =
    ahead.length === 0 || last < 0
      ? []
      : [
          actualPoints[last],
          ...ahead.map((point, index) => ({ x: x(last + 1 + index), y: y(point.total) })),
        ];
  const ticks = [0, 0.5, 1].map((share) => ({
    y: y(ceiling * share),
    label: money(ceiling * share, report.currency),
  }));
  const ends =
    all.length === 0
      ? []
      : all.length === 1
        ? [{ x: x(0), label: dayShort(all[0].day) }]
        : [
            { x: x(0), label: dayShort(all[0].day) },
            { x: x(all.length - 1), label: dayShort(all[all.length - 1].day) },
          ];
  return {
    actual: path(actualPoints),
    forecast: path(forecastPoints),
    newest:
      last < 0
        ? null
        : { x: actualPoints[last].x, y: actualPoints[last].y, partial: report.days[last].partial },
    ticks,
    ends,
    ceiling,
  };
}
// #endregion spend-line

// #region spend-donut
export type DonutArc = { name: string; path: string; share: number; tone: number };

/**
 * The donut: one arc per slice, clockwise from the top, each a share of the
 * whole. A slice that is the whole is drawn as two halves, because an SVG arc
 * whose ends meet draws nothing.
 */
export function donutArcs(slices: CostSlice[], radius = 80, inner = 50): DonutArc[] {
  const total = slices.reduce((sum, slice) => sum + slice.cost, 0);
  if (total <= 0) return [];
  const centre = radius;
  const point = (angle: number, r: number) => {
    const theta = (angle - 90) * (Math.PI / 180);
    return `${(centre + r * Math.cos(theta)).toFixed(2)},${(centre + r * Math.sin(theta)).toFixed(2)}`;
  };
  const arc = (from: number, to: number) => {
    const large = to - from > 180 ? 1 : 0;
    return [
      `M${point(from, radius)}`,
      `A${radius},${radius} 0 ${large} 1 ${point(to, radius)}`,
      `L${point(to, inner)}`,
      `A${inner},${inner} 0 ${large} 0 ${point(from, inner)}`,
      'Z',
    ].join(' ');
  };
  let start = 0;
  return slices.map((slice, index) => {
    const sweep = (slice.cost / total) * 360;
    const end = start + sweep;
    const path =
      sweep >= 359.99 ? `${arc(0, 180)} ${arc(180, 359.99)}` : arc(start, Math.max(start, end));
    start = end;
    return {
      name: slice.name,
      path,
      share: slice.share,
      tone: slice.name === 'Others' ? 0 : index + 1,
    };
  });
}
// #endregion spend-donut

// #region spend-bars
/** Each type's bar as a share of the longest, so the longest fills the track. */
export function typeBars(
  types: CostKind[]
): { label: string; resources: number; cost: number; share: number }[] {
  const most = Math.max(0, ...types.map((kind) => kind.resources));
  return types.map((kind) => ({
    label: kind.label,
    resources: kind.resources,
    cost: kind.cost,
    share: most === 0 ? 0 : kind.resources / most,
  }));
}
// #endregion spend-bars
