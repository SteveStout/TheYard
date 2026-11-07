/**
 * The box's live panels (ADR: The box reports itself, the addendum on the live panels): the ring the site holds
 * of the box's last minute, laid out as System Monitor draws its Resources tab. Each panel is sixty seconds wide
 * with now at the right; the cores, the memory and swap and the GPUs are shares from 0 to 100, and the network
 * scales to the window's peak. The server owns every figure; these functions only place them.
 *
 * No React in here: plain functions over the live answer, each a rule a test can read.
 */
import { linePath } from '../cloud';
import { request } from '../request';

/** One live sample, as GET /api/admin/cloud/box/live writes it (BoxSnapshot.cs, BoxLiveSample). */
export type BoxLiveSample = {
  at: string;
  cores: number[];
  memory_used_b: number;
  memory_total_b: number;
  swap_used_b: number;
  swap_total_b: number;
  nets: { name: string; in_bps: number; out_bps: number }[];
  gpus: { index: number; load: number }[];
};

/** GET /api/admin/cloud/box/live (BoxHandlers.cs, BoxLiveReport). */
export type BoxLiveReport = {
  as_of_ms: number;
  cadence_seconds: number;
  silent_seconds: number | null;
  samples: BoxLiveSample[];
};

/** One line of a panel: its name and its path. */
export type LiveLine = { name: string; path: string };

/** A panel: its title line with the newest figures, its lines, and the top of its axis in words. */
export type LivePanel = {
  key: string;
  title: string;
  figures: string;
  lines: LiveLine[];
  top: string;
};

/** The panel's box, in SVG units. */
export const LIVE_BOX = { width: 300, height: 90 };

/** How wide a panel's window is, and past how long without a sample it reads blue. */
export const WINDOW_SECONDS = 60;
export const STALE_SECONDS = 10;

/** The live answer, or null when the read failed: the one read the panels make, every two seconds. */
export async function readBoxLive(): Promise<BoxLiveReport | null> {
  try {
    // A read nobody clicked for, every two seconds: it never locks the screen.
    const response = await request('/api/admin/cloud/box/live', undefined, { background: true });
    return response.ok ? ((await response.json()) as BoxLiveReport) : null;
  } catch {
    return null;
  }
}

/** Whether the panels should ask for the ring now: only while the card is open and the tab is in view. */
export function shouldPoll(
  mounted: boolean,
  visibility: DocumentVisibilityState | string
): boolean {
  return mounted && visibility === 'visible';
}

/** Whether the live answer is stale: never read, never posted, or silent past the stale line. */
export function isStale(report: BoxLiveReport | null): boolean {
  return report === null || report.silent_seconds === null || report.silent_seconds > STALE_SECONDS;
}

/** A rate the way System Monitor says it: "16.9 KiB/s". */
export function rateWords(bytesPerSecond: number): string {
  const units = ['B/s', 'KiB/s', 'MiB/s', 'GiB/s'];
  let value = bytesPerSecond;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  return unit === 0 ? `${Math.round(value)} ${units[0]}` : `${value.toFixed(1)} ${units[unit]}`;
}

/** The x of a sample: its age against the newest, 60 s at the left and now at the right. */
function xOf(at: string, newestMs: number): number {
  const age = (newestMs - Date.parse(at)) / 1000;
  return LIVE_BOX.width * (1 - Math.min(WINDOW_SECONDS, Math.max(0, age)) / WINDOW_SECONDS);
}

/** A share from 0 to 100 as a y. */
function yOfShare(share: number): number {
  return LIVE_BOX.height * (1 - Math.min(100, Math.max(0, share)) / 100);
}

/** A share of a total to one place, or 0 when there is no total. */
export function shareOf(used: number, total: number): number {
  return total > 0 ? Math.round((1000 * used) / total) / 10 : 0;
}

/** The top of the network axis: the window's peak, loopback left out, rounded up to a power of two, never under 1 KiB/s. */
export function networkTop(samples: BoxLiveSample[]): number {
  const peak = Math.max(
    1024,
    ...samples.flatMap((s) =>
      s.nets.filter((n) => n.name !== 'lo').flatMap((n) => [n.in_bps, n.out_bps])
    )
  );
  return 2 ** Math.ceil(Math.log2(peak));
}

/** The four panels, drawn from the ring, oldest sample first. */
export function livePanels(report: BoxLiveReport): LivePanel[] {
  const samples = [...report.samples].sort((a, b) => Date.parse(a.at) - Date.parse(b.at));
  const newestMs =
    samples.length === 0 ? report.as_of_ms : Date.parse(samples[samples.length - 1].at);
  const newest = samples[samples.length - 1];
  const share = (values: (s: BoxLiveSample) => number) =>
    linePath(samples.map((s) => ({ x: xOf(s.at, newestMs), y: yOfShare(values(s)) })));
  const cores = newest?.cores.length ?? 0;
  const gpuIndexes = [...new Set(samples.flatMap((s) => s.gpus.map((g) => g.index)))].sort();
  const top = networkTop(samples);
  const inOf = (s: BoxLiveSample) =>
    s.nets.filter((n) => n.name !== 'lo').reduce((sum, n) => sum + n.in_bps, 0);
  const outOf = (s: BoxLiveSample) =>
    s.nets.filter((n) => n.name !== 'lo').reduce((sum, n) => sum + n.out_bps, 0);
  const rate = (value: (s: BoxLiveSample) => number) =>
    linePath(
      samples.map((s) => ({
        x: xOf(s.at, newestMs),
        y: LIVE_BOX.height * (1 - Math.min(1, value(s) / top)),
      }))
    );
  return [
    {
      key: 'cores',
      title: 'Processor, each core, live',
      figures:
        newest === undefined ? '' : `${newest.cores.map((load) => load.toFixed(1)).join(' · ')} %`,
      lines: Array.from({ length: cores }, (_, i) => ({
        name: `CPU${i + 1}`,
        path: share((s) => s.cores[i] ?? 0),
      })),
      top: '100 %',
    },
    {
      key: 'memory',
      title: 'Memory and swap, live',
      figures:
        newest === undefined
          ? ''
          : `${shareOf(newest.memory_used_b, newest.memory_total_b)} % · swap ${shareOf(newest.swap_used_b, newest.swap_total_b)} %`,
      lines: [
        { name: 'memory', path: share((s) => shareOf(s.memory_used_b, s.memory_total_b)) },
        { name: 'swap', path: share((s) => shareOf(s.swap_used_b, s.swap_total_b)) },
      ],
      top: '100 %',
    },
    {
      key: 'network',
      title: 'Network, live',
      figures:
        newest === undefined
          ? ''
          : `in ${rateWords(inOf(newest))} · out ${rateWords(outOf(newest))}`,
      lines: [
        { name: 'receiving', path: rate(inOf) },
        { name: 'sending', path: rate(outOf) },
      ],
      top: rateWords(top),
    },
    {
      key: 'gpus',
      title: 'GPUs, live',
      figures:
        newest === undefined
          ? ''
          : newest.gpus.map((g) => `GPU ${g.index} ${Math.round(g.load)} %`).join(' · '),
      lines: gpuIndexes.map((index) => ({
        name: `GPU ${index}`,
        path: share((s) => s.gpus.find((g) => g.index === index)?.load ?? 0),
      })),
      top: '100 %',
    },
  ];
}
