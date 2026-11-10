/**
 * One number of the quality history as a line (ADR-130, Quality you can watch): the fence the API fills from
 * data/quality-history.json, read into points, and the arithmetic a chart draws from them. Plain TypeScript with
 * no React import, as everything under src/lib. The versions sit evenly along the bottom, not by date, because a
 * busy day ships ten of them and a quiet week none; the marker is the version a record names, drawn where its
 * point is.
 */

// #region quality-trend
/** One reading: the day, the version (blank for a pre-scan reading), the value and who measured it. */
export type TrendPoint = { date: string; version: string; value: number; preScan: boolean };

/** A fence read whole: what the number is, which way it may move, the version to mark, and the readings. */
export type QualityTrendData = {
  label: string;
  direction: string;
  marker: string;
  points: TrendPoint[];
};

/**
 * Reads a `quality-trend` fence. Its first line is `label | direction | marker version`; every other line is
 * `date | version | value | source`, as the API writes it. A line it cannot read is left out, never guessed.
 */
export function parseTrend(fence: string): QualityTrendData {
  const [head = '', ...lines] = fence.split('\n').filter((line) => line.trim() !== '');
  const [label = '', direction = '', marker = ''] = head.split('|').map((cell) => cell.trim());
  const points = lines
    .map((line) => line.split('|').map((cell) => cell.trim()))
    .filter((cells) => cells.length >= 4 && cells[2] !== '' && Number.isFinite(Number(cells[2])))
    .map(([date, version, value, source]) => ({
      date,
      version,
      value: Number(value),
      preScan: source === 'pre-scan',
    }));
  return { label, direction, marker, points };
}

/** The drawing box, in viewBox units, before it is fitted to the width it is given. */
export const TREND_BOX = { width: 640, height: 200, top: 18, right: 18, bottom: 30, left: 52 };
type TrendBox = typeof TREND_BOX;

/** Where each point sits, the path through the measured ones, the dashed path through the pre-scan's, and the labels. */
export function trendGeometry(data: QualityTrendData, box: TrendBox = TREND_BOX) {
  const values = data.points.map((p) => p.value);
  const low = values.length ? Math.min(...values) : 0;
  const high = values.length ? Math.max(...values) : 1;
  const span = high - low || Math.max(Math.abs(high), 1);
  const floor = low - span * 0.08;
  const top = high + span * 0.08;
  const width = box.width - box.left - box.right;
  const tall = box.height - box.top - box.bottom;
  const step = data.points.length > 1 ? width / (data.points.length - 1) : 0;
  const at = data.points.map((p, i) => ({
    ...p,
    x: Math.round((box.left + i * step) * 10) / 10,
    y: Math.round((box.top + tall - ((p.value - floor) / (top - floor)) * tall) * 10) / 10,
  }));
  const path = (points: typeof at) =>
    points.map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x} ${p.y}`).join(' ');
  const lastPre = at.reduce((last, p, i) => (p.preScan ? i : last), -1);
  const marked = at.find((p) => p.version === data.marker);
  const first = at[0];
  const last = at[at.length - 1];
  return {
    points: at,
    measured: path(at.slice(Math.max(lastPre, 0))),
    preScan: lastPre > 0 ? path(at.slice(0, lastPre + 1)) : '',
    marker: marked ? { x: marked.x, y: marked.y, label: `${data.marker}, the ratchet` } : null,
    yLabels: [
      { y: box.top + tall, text: format(low) },
      { y: box.top, text: format(high) },
    ],
    xLabels:
      first && last
        ? [
            { x: first.x, text: first.version || first.date.slice(5), anchor: 'start' as const },
            { x: last.x, text: last.version || last.date.slice(5), anchor: 'end' as const },
          ]
        : [],
    along: at
      .filter((_, i) => i === 0 || i === at.length - 1 || at[i].version === data.marker)
      .map((p) => ({ key: `${p.version}-${p.date}`, x: p.x, major: true })),
  };
}

/** A value as the page writes it: whole numbers with no decimals, ratios to two places. */
export function format(value: number): string {
  return Number.isInteger(value) ? String(value) : value.toFixed(2);
}
// #endregion quality-trend
