/**
 * What the Traffic card says, in plain words (ADR: The Admin tab, as a
 * product, the addendum on the traffic card in plain words). The card is read
 * by somebody who did not build it, so nothing on it is written in status
 * codes or percentiles without the plain word first: a server error is the
 * site's fault, a turned-away request is the visitor's, the typical answer is
 * the median and the slow answers are the ninety-fifth.
 *
 * No React in here. The four blocks over the charts, the three questions the
 * charts are titled with, and the sentence that makes a flat zero read as good
 * news are decided by plain functions over the slots the charts are drawn
 * from, so the words and the lines cannot disagree and the words can be tested.
 */
import { SLOW_P95_MS } from './statTiles';
import type { TrafficSlot } from './machineChart';

export type TrafficTotals = {
  requests: number;
  server_errors: number;
  client_errors: number;
  slowest_p95_ms: number | null;
};

export type TrafficBlock = {
  key: 'requests' | 'typical' | 'slow' | 'errors';
  label: string;
  value: string;
  detail: string;
  /** A state, never an identity: good news, bad news, or neither. */
  tone: 'good' | 'bad' | 'plain';
  /** The tone as a word, because a colour alone says nothing to somebody who cannot see it. */
  word: string | null;
};

// #region traffic-words
/** The three charts, each titled with the question it answers and one sentence on how to read it. */
export const TRAFFIC_CHARTS = {
  errors: {
    title: 'Did anything fail?',
    read: 'Errors per minute. A server error is the site’s fault and should be zero. A turned-away request is the visitor’s: a wrong address, or not signed in.',
    unit: 'errors / min',
    server: 'Server errors (5xx, the site’s fault)',
    turnedAway: 'Turned away (4xx, the visitor’s)',
  },
  requests: {
    title: 'How busy is it?',
    read: 'Requests per minute. A flat line at zero means the site was up and nobody asked for anything.',
    unit: 'requests / min',
    requests: 'Requests',
  },
  timing: {
    title: 'How fast does it answer?',
    read: 'Milliseconds to answer, lower is better. The typical request, and the slow ones: 19 in 20 requests were faster than the upper line.',
    unit: 'ms',
    typical: 'Typical request (median)',
    slow: 'Slow requests (95th percentile)',
  },
} as const;

/**
 * The typical answer over the stretch: the middle one of the medians the
 * slots hold. A median of every request in the window is not something the
 * slots can give, and the card does not pretend to it: the detail line says
 * "in the typical" minute or stretch.
 */
export function typicalMs(slots: TrafficSlot[]): number | null {
  const medians = slots
    .map((slot) => slot.p50_ms)
    .filter((value): value is number => value !== null)
    .sort((a, b) => a - b);
  if (medians.length === 0) return null;
  // Nearest rank: the middle one of an odd count, the lower of the two middle ones of an even count.
  return medians[Math.ceil(medians.length / 2) - 1];
}

const count = (value: number, one: string, many: string) =>
  `${value.toLocaleString('en-US')} ${value === 1 ? one : many}`;

/**
 * The four blocks over the charts. `stretch` is the window in words, lower
 * case, "last hour". Only a state takes a status colour: no server errors is
 * good news and any is bad news; an answer under the speed tile's own amber
 * line is fast. A slow stretch is left plain here, because the alarm is the
 * speed tile's, which knows about a cold start and this block does not.
 */
export function trafficBlocks(
  slots: TrafficSlot[],
  totals: TrafficTotals,
  stretch: string,
  hourly: boolean
): TrafficBlock[] {
  const typical = typicalMs(slots);
  const slow = totals.slowest_p95_ms;
  const fast = (value: number | null) => value !== null && value < SLOW_P95_MS;
  const each = hourly ? 'minute' : 'stretch';
  const turnedAway =
    totals.client_errors === 0
      ? 'none were turned away'
      : `${count(totals.client_errors, 'more was', 'more were')} turned away (bad address or not signed in)`;
  return [
    {
      key: 'requests',
      label: 'Requests',
      value: totals.requests.toLocaleString('en-US'),
      detail: `${totals.requests === 1 ? 'request' : 'requests'} in the ${stretch}: pages and API calls`,
      tone: 'plain',
      word: null,
    },
    {
      key: 'typical',
      label: 'Typical answer',
      value: typical === null ? 'none' : `${typical.toLocaleString('en-US')} ms`,
      detail:
        typical === null
          ? 'nobody asked for anything'
          : `half of requests were faster, in the typical ${each}`,
      tone: fast(typical) ? 'good' : 'plain',
      word: fast(typical) ? 'fast' : null,
    },
    {
      key: 'slow',
      label: 'Slow answers',
      value: slow === null ? 'none' : `${slow.toLocaleString('en-US')} ms`,
      detail:
        slow === null
          ? 'nobody asked for anything'
          : `19 in 20 requests beat this, in the slowest ${each}`,
      tone: fast(slow) ? 'good' : 'plain',
      word: fast(slow) ? 'fast' : null,
    },
    {
      key: 'errors',
      label: 'Server errors',
      value: totals.server_errors.toLocaleString('en-US'),
      detail: turnedAway,
      tone: totals.server_errors === 0 ? 'good' : 'bad',
      word: totals.server_errors === 0 ? 'none' : 'needs attention',
    },
  ];
}

/**
 * The sentence over the fail chart. Most hours that chart is a flat line at
 * zero, and a flat line reads as "nothing loaded"; said in words, zero reads
 * as the good news it is.
 */
export function failSentence(
  totals: TrafficTotals,
  stretch: string
): { good: boolean; text: string } {
  return totals.server_errors === 0
    ? { good: true, text: `No server errors in the ${stretch}.` }
    : {
        good: false,
        text: `${count(totals.server_errors, 'server error', 'server errors')} in the ${stretch}.`,
      };
}
// #endregion traffic-words
