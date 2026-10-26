/**
 * Does:      Says how a Cloud infrastructure card draws time (ADR: One frame for every resource, the addendum on how
 *            a card draws time): which panels a chart splits into, which marks are full-height lines and which are
 *            counted ticks on the time axis, the day labels and the "now" mark, the peak band of a wide window, what
 *            a narrow axis joins or drops, and what the legend still says once the chart labels its own lines. Plain
 *            functions over the server's chart.
 * Does not:  Draw anything (CloudChart.tsx and CloudPanel.tsx do), read a route, or decide a tone or a line (the
 *            server does). No React in here.
 * Used by:   src/components/admin/CloudChart/CloudChart.tsx, src/components/admin/CloudChart/CloudPanel.tsx.
 */
import { newestValue, type CloudReport } from './cloud';

export type Chart = CloudReport['chart'];
export type ChartSeries = Chart['series'][number];
export type ChartMark = Chart['marks'][number];

const HOUR = 3_600_000;
const DAY = 86_400_000;
const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

/** The windows that plot each bucket's average with its peak, and draw a roll as a tick rather than a line. */
export function isWide(window: string): boolean {
  return window === '7d' || window === '30d';
}

/**
 * The window a chart actually spans, by its own start and end rather than the tab's choice: a card whose chart is
 * always thirty days (the share's, the blob's, the campaign's) is drawn as thirty days whatever the tab asks for.
 */
export function windowOf(startMs: number, endMs: number): string {
  const span = endMs - startMs;
  return span > 8 * DAY ? '30d' : span > 2 * DAY ? '7d' : '24h';
}

/** One panel of a chart: the series it draws and the lines it is read against, each its own. */
export type Panel = {
  name: string;
  series: ChartSeries[];
  amber: number | null;
  red: number | null;
};

/**
 * A chart whose series carry their own lines is one panel a series, each against its own amber and red, so no line
 * belongs to only one of two series on one plot. A chart whose series carry none is one panel against the chart's
 * red line, as it always was.
 */
export function panelsOf(chart: Chart): Panel[] {
  const own = chart.series.some((s) => s.amber != null || s.red != null);
  if (!own) {
    return [
      {
        name: chart.series.map((s) => s.name).join(' and '),
        series: chart.series,
        amber: null,
        red: chart.red_line,
      },
    ];
  }
  return chart.series.map((s) => ({
    name: s.name,
    series: [s],
    amber: s.amber ?? null,
    red: s.red ?? null,
  }));
}

/** A tick on the time axis: one roll, one change or one tally, or several within an hour of each other, counted. */
export type Tick = {
  atMs: number;
  kind: 'deploy' | 'change' | 'count';
  count: number;
  titles: string[];
};

/**
 * The marks drawn as ticks on the time axis. A tally (a bucket's throttled requests, a day's replies) and a change
 * (an Activity Log entry that is not a restart, a configuration write above all) are always ticks. A roll is a tick
 * at seven and thirty days, where twenty rolls would otherwise bury the lines, and a full-height line with its
 * shading at an hour or a day. Marks of one kind within an hour of the one before are one tick with their count.
 */
export function ticksOf(marks: ChartMark[], window: string): Tick[] {
  const wide = isWide(window);
  const ticks: Tick[] = [];
  for (const kind of ['deploy', 'change', 'count'] as const) {
    const picked = marks
      .filter((m) =>
        kind === 'deploy'
          ? wide && m.kind === 'deploy'
          : kind === 'change'
            ? m.kind === 'change'
            : isTally(m)
      )
      .slice()
      .sort((a, b) => a.at_ms - b.at_ms);
    let open: Tick | null = null;
    let last = 0;
    for (const mark of picked) {
      if (open !== null && mark.at_ms - last <= HOUR) {
        open.count += 1;
        open.titles.push(mark.title);
      } else {
        open = { atMs: mark.at_ms, kind, count: 1, titles: [mark.title] };
        ticks.push(open);
      }
      last = mark.at_ms;
    }
  }
  return ticks.sort((a, b) => a.atMs - b.atMs);
}

/**
 * Ticks of one kind closer on the drawn axis than a tick is wide, joined into one with their counts summed and their
 * titles kept, so a phone's narrow axis never prints two counts on top of each other.
 */
export function joinClose(ticks: Tick[], x: (atMs: number) => number, minPx: number): Tick[] {
  const joined: Tick[] = [];
  for (const tick of ticks) {
    const last = [...joined].reverse().find((t) => t.kind === tick.kind);
    if (last !== undefined && x(tick.atMs) - x(last.atMs) < minPx) {
      last.count += tick.count;
      last.titles = [...last.titles, ...tick.titles];
    } else {
      joined.push({ ...tick, titles: [...tick.titles] });
    }
  }
  return joined;
}

/**
 * Labels kept only where they do not run into a neighbour on the drawn axis, read from the right, so the newest days
 * keep their names and a sliver of the oldest day gives way first.
 */
export function spaced<T extends { atMs: number }>(
  labels: T[],
  x: (atMs: number) => number,
  minPx: number
): T[] {
  const kept: T[] = [];
  for (const label of [...labels].reverse()) {
    const next = kept.at(-1);
    if (next === undefined || x(next.atMs) - x(label.atMs) >= minPx) kept.push(label);
  }
  return kept.reverse();
}

/** A mark that is neither a roll, a change nor a restart: a tally, drawn as a tick. */
function isTally(mark: ChartMark): boolean {
  return mark.kind !== 'deploy' && mark.kind !== 'event' && mark.kind !== 'change';
}

/** The marks drawn at full height: a restart or maintenance, always; a roll only at an hour or a day. */
export function fullLines(marks: ChartMark[], window: string): ChartMark[] {
  return marks.filter((m) => m.kind === 'event' || (m.kind === 'deploy' && !isWide(window)));
}

/** Whether each roll's span is shaded behind the lines: at an hour and a day only. */
export function bandsShown(window: string): boolean {
  return !isWide(window);
}

/** A label under the time axis: the day it names and where it sits. */
export type DayLabel = { atMs: number; text: string };

/**
 * The days the axis names, in UTC like every bucket. At a day and a week every day is named, under the middle of
 * the part of it the window shows (a sliver of under six hours is left unnamed); at thirty days Mondays and Fridays,
 * at their midnight, where thirty names would crowd. Short names ("Tu 20") on a narrow chart.
 */
export function dayLabels(
  startMs: number,
  endMs: number,
  window: string,
  short: boolean
): DayLabel[] {
  const labels: DayLabel[] = [];
  for (let at = Math.floor(startMs / DAY) * DAY; at <= endMs; at += DAY) {
    const day = new Date(at).getUTCDay();
    const name = `${short ? WEEKDAYS[day].slice(0, 2) : WEEKDAYS[day]} ${new Date(at).getUTCDate()}`;
    if (window === '30d') {
      if ((day === 1 || day === 5) && at >= startMs) labels.push({ atMs: at, text: name });
      continue;
    }
    const from = Math.max(at, startMs);
    const to = Math.min(at + DAY, endMs);
    if (to - from < 6 * HOUR) continue;
    labels.push({ atMs: (from + to) / 2, text: name });
  }
  return labels;
}

/** The midnights inside the window, where the light day lines are drawn. */
export function midnights(startMs: number, endMs: number): number[] {
  const out: number[] = [];
  for (let at = Math.ceil(startMs / DAY) * DAY; at <= endMs; at += DAY) out.push(at);
  return out;
}

/**
 * The band between each bucket's average and its peak, as one closed path a run, broken where either is missing,
 * so a spike averaged into an hour still reads.
 */
export function peakBand(
  points: ChartSeries['points'],
  x: (atMs: number) => number,
  y: (percent: number) => number
): string {
  const runs: { x: number; low: number; high: number }[][] = [];
  let run: { x: number; low: number; high: number }[] = [];
  for (const p of points) {
    const peak = p.peak ?? null;
    if (p.value === null || peak === null) {
      if (run.length > 0) runs.push(run);
      run = [];
      continue;
    }
    run.push({ x: x(p.at_ms), low: y(p.value), high: y(Math.max(p.value, peak)) });
  }
  if (run.length > 0) runs.push(run);
  return runs
    .filter((r) => r.length > 1)
    .map((r) => {
      const top = r.map((q, i) => `${i === 0 ? 'M' : 'L'} ${q.x.toFixed(1)} ${q.high.toFixed(1)}`);
      const bottom = r
        .slice()
        .reverse()
        .map((q) => `L ${q.x.toFixed(1)} ${q.low.toFixed(1)}`);
      return [...top, ...bottom, 'Z'].join(' ');
    })
    .join(' ');
}

/** The newest reading of a series as its right-edge label: "now 6 %", or nothing when none was read. */
export function nowLabel(series: ChartSeries): string {
  const newest = newestValue(series.points.map((p) => p.value));
  return newest === null ? '' : `now ${Math.round(newest)} %`;
}

/** A line's label as the panel prints it beside the line: "amber 60", "red 85". */
export function lineLabel(kind: 'amber' | 'red', level: number): string {
  return `${kind} ${Number.isInteger(level) ? level : level.toFixed(1)}`;
}

/**
 * What the legend still says once the chart names its own series, labels its own lines and keys its own marks:
 * every chip of the server's legend but the colour of a series ("teal CPU"), the dashed line ("dashed 80 %"), the
 * roll ("gold deploys ...") and the red mark of an Azure event, a capped hour or a throttle, each now on the chart.
 */
export function legendRest(legend: string): string[] {
  return legend
    .split(' · ')
    .map((part) => part.trim())
    .filter((part) => part.length > 0)
    .filter(
      (part) =>
        !/^(teal|green|gold|dashed) /.test(part) &&
        !/^red (Azure restart|event|429s|capped hour)$/.test(part)
    );
}

/** The time axis's words for the marks on it, in the order the chart draws them. */
export function markKey(chart: Chart, window: string): string[] {
  const keys: string[] = [];
  const ticks = ticksOf(chart.marks, window);
  if (ticks.some((t) => t.kind === 'deploy'))
    keys.push(
      'gold: the rolls within an hour of each other, counted; the day view shades each roll'
    );
  if (!isWide(window) && chart.marks.some((m) => m.kind === 'deploy'))
    keys.push('gold line: a roll, shaded while it runs');
  if (ticks.some((t) => t.kind === 'change'))
    keys.push('grey tick: an app’s settings written, counted, hover for each');
  if (ticks.some((t) => t.kind === 'count'))
    keys.push('red tick: a tally in that bucket, hover for it');
  const events = chart.marks.filter((m) => m.kind === 'event').length;
  keys.push(
    events === 0
      ? 'a restart or maintenance, at full height: none in this window'
      : `a restart or maintenance, at full height: ${events}`
  );
  return keys;
}
