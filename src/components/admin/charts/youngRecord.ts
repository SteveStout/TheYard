/**
 * The words for where a kept window's charts start, when the window reaches
 * back further than the record does. Plain functions over the slots, no
 * drawing; they sit beside the charts because the traffic and machines cards
 * both print this sentence under their charts.
 */
import { type KeptBucket, LEAST_SLOTS } from '../../../lib/machineChart';

/** The date and time a kept window's chart starts at, for the sentence that says so. */
export function startLabel(at: string): string {
  return new Date(at).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

/**
 * The sentence for a window that reaches back further than the record does.
 * The chart starts at the first reading, unless that would leave fewer than
 * LEAST_SLOTS slots; then it starts LEAST_SLOTS buckets back from now. The two
 * cases need different sentences, or the page states the wrong start date.
 */
export function youngRecord(drawn: { at: string; bucket: KeptBucket | null }[]): string {
  const first = drawn.find((slot) => slot.bucket !== null);
  return first === undefined || first.at === drawn[0].at
    ? `The record is younger than the window, so the charts start at its first reading, ${startLabel(drawn[0].at)}`
    : `The record is younger than the window: its first reading is ${startLabel(first.at)}, and the charts start ${LEAST_SLOTS} buckets back from now, at ${startLabel(drawn[0].at)}`;
}
