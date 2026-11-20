/**
 * Does:      Turns /api/pulse into the rows a dark live panel draws (ADR: The sales pages set the site's look): the review
 *            route's first screen ("TheYard, right now": how fast it answers, how long since the last release, the
 *            month's Azure bill) and TheYard's case card ("TheYard, this week": requests answered, releases and people
 *            who visited). Never dead: a figure never read hides its row, never a dash, an empty cell, NaN or 0 ms; a
 *            read that fails keeps the last answer, and the panel says "as of HH:MM" under it.
 * Does not:  Fetch (usePulse.ts does), draw anything (NowPanel.tsx does) or count the releases (releaseFacts.ts does).
 * Used by:   usePulse.ts, ReviewVerdict.tsx, ReviewWork.tsx, nowPanel.test.ts.
 */
import { dayWords, moneyWords, pulseFrom, UNREAD, type Pulse } from './pulse';

/** One row of a live panel: its label, its value and the line under the value. */
export type NowRow = { key: string; label: string; value: string; under: string };

/** What the site holds of /api/pulse: the last answer read, and whether the newest read failed. */
export type PulseReading = { pulse: Pulse | null; failed: boolean };

/** Nothing read yet. */
export const NO_READING: PulseReading = { pulse: null, failed: false };

/**
 * The reading after one more read. An answer of the right shape replaces the last; a failed read (null) keeps the last
 * answer and marks it failed, so a panel says how old it is; an answer of the wrong shape is no reading at all.
 */
export function nextReading(last: PulseReading, answer: unknown): PulseReading {
  if (answer === null) return { pulse: last.pulse, failed: true };
  return { pulse: pulseFrom(answer), failed: false };
}

/** "as of 14:05" under a panel whose newest read failed, the last answer's own time; null while the reading is fresh. */
export function asOfLine(reading: PulseReading, timeZone?: string): string | null {
  if (!reading.failed || reading.pulse === null) return null;
  const at = new Date(reading.pulse.read_at_ms);
  if (Number.isNaN(at.getTime())) return null;
  const time = at.toLocaleTimeString('en-GB', {
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
    timeZone,
  });
  return `as of ${time}`;
}

/** How long ago, as the panel says it: "8 min ago", "2 h 45 min ago", "3 d 4 h ago"; never under a minute. */
export function agoWords(ms: number): string {
  const minutes = Math.max(1, Math.floor(Math.max(0, ms) / 60_000));
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} h ${minutes - hours * 60} min ago`;
  const days = Math.floor(hours / 24);
  return `${days} d ${hours - days * 24} h ago`;
}

/** A count read: a finite number above zero, which a panel draws; anything else hides its row. */
const counted = (value: number | null | undefined): value is number =>
  typeof value === 'number' && Number.isFinite(value) && value > 0;

/** The first screen's panel, "TheYard, right now": each row only when its figure was read. */
export function rightNowRows(pulse: Pulse | null, nowMs: number): NowRow[] {
  if (pulse === null) return [];
  const rows: NowRow[] = [];
  if (counted(pulse.usual_p95_ms_24h)) {
    rows.push({
      key: 'p95',
      label: 'Answering in',
      value: `${pulse.usual_p95_ms_24h.toLocaleString('en-US')} ms`,
      under: 'p95, last 24 hours',
    });
  }
  if (Number.isFinite(pulse.uptime_seconds) && pulse.uptime_seconds >= 0 && pulse.version !== '') {
    const released = pulse.read_at_ms - pulse.uptime_seconds * 1000;
    rows.push({
      key: 'release',
      label: 'Last release',
      value: agoWords(nowMs - released),
      under: pulse.version === 'dev' ? 'dev build' : `version ${pulse.version}`,
    });
  }
  const bill = moneyWords(pulse.month_to_date, pulse.currency);
  if (bill !== UNREAD) {
    rows.push({ key: 'bill', label: 'Azure bill', value: bill, under: 'this month so far' });
  }
  return rows;
}

/** TheYard card's panel, "TheYard, this week": the releases come from the build, the rest from the pulse. */
export function thisWeekRows(pulse: Pulse | null, releases: number): NowRow[] {
  const rows: NowRow[] = [];
  if (pulse !== null && counted(pulse.requests_24h)) {
    rows.push({
      key: 'requests',
      label: 'Requests answered',
      value: pulse.requests_24h.toLocaleString('en-US'),
      under: 'last 24 hours',
    });
  }
  if (counted(releases)) {
    rows.push({
      key: 'releases',
      label: 'Releases',
      value: releases.toLocaleString('en-US'),
      under: 'last 7 days, one gate each',
    });
  }
  if (pulse !== null && counted(pulse.people_all_time)) {
    const since = dayWords(pulse.counted_from);
    rows.push({
      key: 'people',
      label: 'People who visited',
      value: pulse.people_all_time.toLocaleString('en-US'),
      under: since === UNREAD ? 'all time' : `since ${since}`,
    });
  }
  return rows;
}
