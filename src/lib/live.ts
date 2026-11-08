/**
 * A card's live ring (ADR: Live dials where the source allows): the one read the page makes of
 * GET /api/admin/cloud/{key}/live, as a background read through the request door, and the words a card's live line
 * says: the source, its grain and the newest reading, or why there is none. The server owns every figure.
 *
 * No React in here: plain functions, each a rule a test can read.
 */
import { request } from './request';

/** One live sample, as the server writes it. */
export type LiveSample = { at: string; values: Record<string, number | null> };

/** GET /api/admin/cloud/{key}/live (ReadLiveHandler.cs, LiveReport). */
export type LiveReport = {
  available: boolean;
  note: string | null;
  grain_seconds: number;
  source: string | null;
  as_of_ms: number;
  samples: LiveSample[];
};

/** The live answer for a card, or null when the read failed. */
export async function readLive(key: string): Promise<LiveReport | null> {
  try {
    // A read nobody clicked for: it never locks the screen.
    const response = await request(`/api/admin/cloud/${key}/live`, undefined, { background: true });
    return response.ok ? ((await response.json()) as LiveReport) : null;
  } catch {
    return null;
  }
}

/** How often to ask: the source's own grain, never faster than every 2 s. */
export function pollMs(report: LiveReport | null): number {
  return Math.max(2, report?.grain_seconds ?? 2) * 1000;
}

/** Whether to keep asking: while the card is open, the tab is in view, and the card has a live source. */
export function keepPolling(
  mounted: boolean,
  visibility: string,
  report: LiveReport | null
): boolean {
  return mounted && visibility === 'visible' && (report === null || report.available);
}

/** The newest sample, or null. */
export function newest(report: LiveReport | null): LiveSample | null {
  if (report === null || report.samples.length === 0) return null;
  return report.samples.reduce((a, b) => (Date.parse(b.at) >= Date.parse(a.at) ? b : a));
}

/** The card's live line: what it reads and how often, then the newest reading, or why there is none. */
export function liveWords(report: LiveReport | null, nowMs: number): string {
  if (report === null) return 'live: not read yet';
  if (!report.available) return `no live source: ${report.note ?? 'none'}`;
  const sample = newest(report);
  const head = `live, every ${report.grain_seconds} s, ${report.source ?? 'its source'}`;
  if (sample === null) return `${head}: the first sample is on its way`;
  const age = Math.max(0, Math.round((nowMs - Date.parse(sample.at)) / 1000));
  const figures = Object.entries(sample.values)
    .map(([name, value]) => `${name} ${value === null ? 'not read' : Number(value.toFixed(1))}`)
    .join(' · ');
  return `${head}: ${figures}, ${age} s ago`;
}
