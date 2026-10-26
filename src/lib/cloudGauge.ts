/**
 * Does:      Gives a dial the geometry the owner approved on the canvas "Admin card chart redesign": an open ring of
 *            three quarters from seven o'clock to five, the arc, each threshold named on the dial by a leader line
 *            from its tick to its words outside the ring ("amber 60 %", "red 80 %", "p95 18 %") with labels on one
 *            side pushed apart, and the one quiet line on how a dial's colour is held.
 * Does not:  Draw the dial (CloudDial.tsx does) or decide its tone or levels (the server does). The summary column's
 *            full ring stays in src/lib/cloud.ts. No React in here.
 * Used by:   src/components/admin/CloudDial/CloudDial.tsx, src/components/admin/CardShell/CardShell.tsx.
 */
import type { CloudDial } from './cloud';

/** The ring opens at the bottom: it starts at seven o'clock and sweeps three quarters clockwise to five. */
export const GAUGE_START = 135;
export const GAUGE_SWEEP = 270;

/** The angle, in degrees clockwise from three o'clock, a percentage sits at; out-of-range readings are pinned to the ends. */
export function gaugeAngle(percent: number): number {
  const clamped = Math.max(0, Math.min(100, percent));
  return GAUGE_START + (GAUGE_SWEEP * clamped) / 100;
}

/** A point on a circle of radius r around (cx, cy) at an angle in degrees. */
export function gaugePoint(
  cx: number,
  cy: number,
  r: number,
  degrees: number
): { x: number; y: number } {
  const radians = (degrees * Math.PI) / 180;
  return { x: cx + r * Math.cos(radians), y: cy + r * Math.sin(radians) };
}

/** The SVG path of the ring from one percentage to another. */
export function gaugeArc(cx: number, cy: number, r: number, from: number, to: number): string {
  const a = gaugeAngle(from);
  const b = gaugeAngle(Math.max(from + 0.01, to));
  const start = gaugePoint(cx, cy, r, a);
  const end = gaugePoint(cx, cy, r, b);
  const large = b - a > 180 ? 1 : 0;
  return `M ${start.x.toFixed(2)} ${start.y.toFixed(2)} A ${r} ${r} 0 ${large} 1 ${end.x.toFixed(2)} ${end.y.toFixed(2)}`;
}

/** One threshold named on the dial: its tick across the ring, its leader line, and where its words sit. */
export type GaugeLabel = {
  key: 'amber' | 'red' | 'p95';
  text: string;
  tick: { x1: number; y1: number; x2: number; y2: number };
  leader: string;
  textX: number;
  textY: number;
  anchor: 'start' | 'end';
};

/** A level as the dial prints it: whole numbers plainly, anything finer to one place. */
function level(value: number): string {
  return Number.isInteger(value) ? `${value}` : value.toFixed(1);
}

/** The gap kept between two labels on one side, in the dial's units. */
export const LABEL_GAP = 15;

/**
 * Where each threshold's words go. Each leaves its tick along the radius, then runs level to its words beside the
 * ring, on the side its tick is on; labels on one side that would touch are pushed apart, top to bottom, and kept
 * inside the drawing.
 */
export function gaugeLabels(
  dial: Pick<CloudDial, 'amber' | 'red' | 'p95'>,
  cx: number,
  cy: number,
  r: number,
  height: number
): GaugeLabel[] {
  const wanted: { key: GaugeLabel['key']; text: string; percent: number }[] = [
    { key: 'amber', text: `amber ${level(dial.amber)} %`, percent: dial.amber },
    { key: 'red', text: `red ${level(dial.red)} %`, percent: dial.red },
  ];
  if (dial.p95 !== null)
    wanted.push({ key: 'p95', text: `p95 ${Math.round(dial.p95)} %`, percent: dial.p95 });
  const placed = wanted.map((w) => {
    const angle = gaugeAngle(w.percent);
    const inner = gaugePoint(cx, cy, r - 9, angle);
    const outer = gaugePoint(cx, cy, r + 9, angle);
    const elbow = gaugePoint(cx, cy, r + 22, angle);
    const right = Math.cos((angle * Math.PI) / 180) >= -0.001;
    return { w, inner, outer, elbow, right, y: elbow.y };
  });
  for (const side of [true, false]) {
    const group = placed.filter((p) => p.right === side).sort((a, b) => a.y - b.y);
    for (let i = 1; i < group.length; i += 1) {
      if (group[i].y - group[i - 1].y < LABEL_GAP) group[i].y = group[i - 1].y + LABEL_GAP;
    }
    const overflow = group.length > 0 ? group[group.length - 1].y - (height - 6) : 0;
    if (overflow > 0) group.forEach((p) => (p.y -= overflow));
    group.forEach((p) => (p.y = Math.max(10, p.y)));
  }
  return placed.map((p) => {
    const textX = p.right ? cx + r + 44 : cx - r - 44;
    const end = p.right ? textX - 4 : textX + 4;
    return {
      key: p.w.key,
      text: p.w.text,
      tick: { x1: p.inner.x, y1: p.inner.y, x2: p.outer.x, y2: p.outer.y },
      leader: `M ${p.outer.x.toFixed(1)} ${p.outer.y.toFixed(1)} L ${p.elbow.x.toFixed(1)} ${p.y.toFixed(1)} L ${end.toFixed(1)} ${p.y.toFixed(1)}`,
      textX,
      textY: p.y + 4,
      anchor: p.right ? 'start' : 'end',
    };
  });
}

/**
 * The quiet line under a dial that says how its colour is held, from the server's hold chip ("held 15 min"): a dial
 * changes colour only after so long past its line. Nothing when the card's tones are not held.
 */
export function holdLine(thresholds: { label: string; tone: string }[]): string {
  const held = thresholds.find((chip) => chip.tone === 'held');
  const minutes = held === undefined ? null : /(\d+)/.exec(held.label)?.[1];
  return minutes == null ? '' : `a dial changes colour only after ${minutes} min past its line`;
}

/** A sixty-minute sparkline as the wire sends it. */
type Spark = { name: string; values: (number | null)[] };

/** A name's subject, the words before its comma, in lower case: "CPU, 60 min" is "cpu". */
function subject(name: string): string {
  return name.split(',')[0].trim().toLowerCase();
}

/**
 * Which sparkline sits under which dial: one whose subject is the dial's own ("CPU, 60 min" under "CPU, right now",
 * "Sampling, 60 h" under "Sampling kept"), so a panel never puts another reading's hour under a dial. Every sparkline
 * that is no dial's own is kept, in its order, for the row under the dials: none is dropped.
 */
export function sparksFor(
  dials: { name: string }[],
  sparks: Spark[]
): { byDial: (Spark | undefined)[]; rest: Spark[] } {
  const used = new Set<number>();
  const byDial = dials.map((dial) => {
    const own = subject(dial.name);
    const at = sparks.findIndex((spark, i) => {
      const of = subject(spark.name);
      return !used.has(i) && of.length > 0 && (own.startsWith(of) || of.startsWith(own));
    });
    if (at < 0) return undefined;
    used.add(at);
    return sparks[at];
  });
  return { byDial, rest: sparks.filter((_, i) => !used.has(i)) };
}
