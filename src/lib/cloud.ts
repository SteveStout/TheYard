/**
 * The Cloud infrastructure cards' shared arithmetic (ADR: The App Service plan, read every minute): the
 * wire every card reads, the dial's geometry, the chart's scales and the axis labels, each a
 * plain function so a test can hold it without a page. No React in here.
 */

// #region cloud-wire
export type CloudTone = 'good' | 'info' | 'warning' | 'alarm';

/** A card's one tone, from the tone rule in Domain (CardTones): the rail's dot and the header's chip. */
export type CardTone = 'fine' | 'amber' | 'red' | 'unread';

/** One threshold as a chip: "CPU amber 60 %". */
export type CloudChip = { label: string; tone: 'amber' | 'red' | 'held' };

/** A piece of text that may link to a resource's portal blade. */
export type CloudLink = { text: string; href: string | null };

export type CloudDial = {
  name: string;
  percent: number | null;
  big: string;
  absolute: string;
  amber: number;
  red: number;
  p95: number | null;
  peak: number | null;
  tone: CloudTone;
};

export type CloudReport = {
  window: string;
  windows: string[];
  available: boolean;
  note: string | null;
  as_of_ms: number;
  read_age_seconds: number | null;
  title: string;
  spec: string | null;
  resource: CloudLink | null;
  dials: CloudDial[];
  sparks: { name: string; values: (number | null)[] }[];
  /** The card's one tone. */
  tone: CardTone;
  /** Where the spec line was read: "ARM sku · this process". */
  spec_source: string | null;
  /** The first dial's figure, which the rail prints beside the card's name. */
  headline: string | null;
  /** The thresholds as chips, in place of the sentence under the dials. */
  thresholds: CloudChip[];
  /** How often the tab reads the card, in seconds. */
  refresh_seconds: number;
  chart: {
    title: string;
    series: { name: string; points: { at_ms: number; value: number | null }[] }[];
    marks: { at_ms: number; kind: 'deploy' | 'event'; title: string }[];
    red_line: number;
    legend: string;
    start_ms: number;
    /** Each deploy as a shaded span, from its first write to the new container answering. */
    bands: { start_ms: number; end_ms: number; title: string }[];
  };
  rows: {
    key: string;
    question: string;
    answer: string;
    detail: CloudLink[];
    tone: CloudTone;
    /** The row's list as a table; no columns and no rows when the row has none. */
    table: { columns: string[]; rows: CloudLink[][] };
    /** Where the row's answer comes from. */
    source: string | null;
  }[];
  readout: {
    title: string;
    uptime_percent: number | null;
    uptime_label: string;
    lines: {
      label: string;
      value: string;
      tone: CloudTone;
      source: string | null;
      /** The name the page finds a line by, so a reworded label never loses it. */
      key?: string | null;
    }[];
    footnote: string;
  };
};

/** The word a row prints beside its answer: green is fine, black is for information, amber is to watch, red is a warning. */
export function toneWord(tone: CloudTone): string {
  switch (tone) {
    case 'good':
      return 'fine';
    case 'warning':
      return 'watch';
    case 'alarm':
      return 'warning';
    default:
      return 'for information';
  }
}
/** What the rail prints beside a card's name: its tone dot and its headline figure. */
export type RailGlance = { tone: CardTone; headline: string | null };

/** A card's rail glance, from its report. */
export function railGlance(report: Pick<CloudReport, 'tone' | 'headline'>): RailGlance {
  return { tone: report.tone, headline: report.headline };
}

/** The chip a card's header prints for its tone. */
/**
 * What a resource link opens, said beside it: the Azure portal, Netlify, or nothing when there is
 * no link or one to a place this page does not know. The sign-in is the reader's own, never the
 * site's.
 */
export function portalSentence(href: string | null, long = false): string {
  if (href === null) return '';
  if (href.includes('portal.azure.com'))
    return long
      ? '(opens the Azure portal in a new tab, with your own Azure sign-in)'
      : 'opens in the Azure portal; your sign-in, not the site’s';
  if (href.includes('netlify.com'))
    return long
      ? '(opens Netlify in a new tab, with your own Netlify sign-in)'
      : 'opens Netlify; your sign-in, not the site’s';
  return '';
}

export function cardToneWord(tone: CardTone): string {
  return tone === 'unread' ? 'not read' : tone;
}

/** A chart's legend as chips: the server's sentence split at its middle dots. */
export function legendChips(legend: string): string[] {
  return legend
    .split(' \u00b7 ')
    .map((part) => part.trim())
    .filter((part) => part.length > 0);
}

/** A sparkline's newest reading, the figure its title line prints, or null when none was read. */
export function newestValue(values: (number | null)[]): number | null {
  for (let i = values.length - 1; i >= 0; i -= 1) {
    const value = values[i];
    if (value !== null && value !== undefined) return value;
  }
  return null;
}
// #endregion cloud-wire

// #region dial-geometry
/** The dial is a whole ring that fills clockwise from twelve o'clock, angles measured clockwise from three. */
export const DIAL_START = -90;
export const DIAL_SWEEP = 360;

/** The angle, in degrees, a percentage sits at on the dial. Out-of-range readings are pinned to the ends. */
export function angleOf(percent: number): number {
  // Just short of a full turn at 100, so an arc from 0 to 100 still has two distinct ends to draw between.
  const clamped = Math.max(0, Math.min(99.99, percent));
  return DIAL_START + (DIAL_SWEEP * clamped) / 100;
}

/** A point on a circle of radius r around (cx, cy) at an angle in degrees. */
export function pointAt(
  cx: number,
  cy: number,
  r: number,
  degrees: number
): { x: number; y: number } {
  const radians = (degrees * Math.PI) / 180;
  return { x: cx + r * Math.cos(radians), y: cy + r * Math.sin(radians) };
}

/** The SVG path of an arc from one percentage to another on a circle. */
export function arcPath(cx: number, cy: number, r: number, from: number, to: number): string {
  const a = angleOf(from);
  const b = angleOf(to);
  const start = pointAt(cx, cy, r, a);
  const end = pointAt(cx, cy, r, b);
  const large = b - a > 180 ? 1 : 0;
  return `M ${start.x.toFixed(2)} ${start.y.toFixed(2)} A ${r} ${r} 0 ${large} 1 ${end.x.toFixed(2)} ${end.y.toFixed(2)}`;
}

/** The tick ring: one tick every 2.5 per cent round the whole ring, a long one every ten. */
export function ticks(): { percent: number; long: boolean }[] {
  return Array.from({ length: 40 }, (_, i) => ({ percent: i * 2.5, long: i % 4 === 0 }));
}
// #endregion dial-geometry

// #region chart-scales
/** The chart's x position for an instant inside the window, from 0 to width. */
export function xOf(atMs: number, startMs: number, endMs: number, width: number): number {
  if (endMs <= startMs) return 0;
  return ((atMs - startMs) / (endMs - startMs)) * width;
}

/** The chart's y position for a percentage, 100 at the top. */
export function yOf(percent: number, height: number): number {
  return height - (Math.max(0, Math.min(100, percent)) / 100) * height;
}

/** A line through the points that carry a value, broken over a gap so a missing minute is never drawn as zero. */
export function linePath(points: { x: number; y: number | null }[]): string {
  let path = '';
  let open = false;
  for (const point of points) {
    if (point.y === null) {
      open = false;
      continue;
    }
    path += `${open ? 'L' : 'M'} ${point.x.toFixed(1)} ${point.y.toFixed(1)} `;
    open = true;
  }
  return path.trim();
}

/**
 * The days the axis names, one tick a day across the window, labelled only on Mondays and
 * Fridays (the owner's standing rule: those are the days kept free), in UTC like every bucket.
 */
export function dayMarks(startMs: number, endMs: number): { atMs: number; label: string | null }[] {
  const marks: { atMs: number; label: string | null }[] = [];
  const day = 86_400_000;
  for (let at = Math.ceil(startMs / day) * day; at <= endMs; at += day) {
    const weekday = new Date(at).getUTCDay();
    marks.push({ atMs: at, label: weekday === 1 ? 'Mon' : weekday === 5 ? 'Fri' : null });
  }
  return marks;
}
// #endregion chart-scales

/**
 * A readout value as the lines it prints on: a list the server joins with a comma and a space
 * (a certificate's four names, say) is one item a line, so a long list never runs past the panel.
 * A number's thousands comma has no space after it and stays whole.
 */
export function valueLines(value: string): string[] {
  return value.split(', ').filter((part) => part.length > 0);
}

/**
 * What goes between two pieces of a row's detail line: nothing when the pieces already meet as one
 * sentence (the first ends in a space, or the second opens with a space or a stop), else a middle
 * dot, so two separate facts never run together as one word.
 */
export function detailJoint(before: string, after: string): string {
  return /\s$/.test(before) || /^[\s,.;:)]/.test(after) ? '' : ' · ';
}
