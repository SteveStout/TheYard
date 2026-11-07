/**
 * The cost card's two new questions (ADR: What Azure charges, the addendum on the spike guard): did a day
 * spike, as eight bars against each day's own spike line, and what watches the meters that can run away, as
 * one tone per meter. The server ships the days, their medians and the rules it found; the line a day must
 * pass and the tone each reading takes come from ranges.ts, so the rule is written once.
 */
import {
  spikeLine,
  spikeTone,
  SPIKE_FLOOR_DOLLARS,
  SPIKE_MULTIPLE,
  type RangeTone,
} from '../ranges';
import type { MeterRow, SpikeDay } from './costReport';
import { dayShort, money } from './costWords';

/** One bar of the spike chart: the day, its height and its line as shares of the chart's top, and its tone. */
export type SpikeBar = {
  day: string;
  label: string;
  cost: number;
  line: number;
  /** The bar's height, 0 to 1 of the chart. */
  height: number;
  /** Where the day's spike line sits, 0 to 1 of the chart. */
  lineAt: number;
  tone: RangeTone;
  partial: boolean;
};

/** The bars oldest first, scaled so the tallest bar or line fills the chart. */
export function spikeBars(days: readonly SpikeDay[]): SpikeBar[] {
  const ordered = [...days].reverse();
  const top = Math.max(0.01, ...ordered.map((day) => Math.max(day.cost, spikeLine(day.median))));
  return ordered.map((day) => {
    const line = spikeLine(day.median);
    return {
      day: day.day,
      label: dayShort(day.day),
      cost: day.cost,
      line,
      height: day.cost / top,
      lineAt: line / top,
      tone: spikeTone(day),
      partial: day.partial,
    };
  });
}

/** The rule in words, built from the same constants the tone reads. */
export function spikeRuleWords(): string {
  return `red on any day over ${SPIKE_MULTIPLE}x the median of the week before it (never under ${money(SPIKE_FLOOR_DOLLARS)})`;
}

/** A day's multiple in words: "17.5x the week's median", or why there is none. */
export function multipleWords(day: SpikeDay): string {
  if (day.median === null || day.multiple === null) return 'no finished week before it yet';
  const week = day.days === 7 ? "the week's" : day.days === 1 ? "1 day's" : `${day.days} days'`;
  return `${day.multiple}x ${week} median ${money(day.median)}`;
}

/** The worst of the newest two days, when one of them is a spike: the sentence the Overview adds. */
export function spikeSentence(days: readonly SpikeDay[] | undefined): string | null {
  const spiked = (days ?? []).slice(0, 2).find((day) => spikeTone(day) === 'bad');
  if (spiked === undefined) return null;
  const driver = spiked.biggest === null ? '' : `, driven by ${spiked.biggest}`;
  return `spike: ${dayShort(spiked.day)} ${money(spiked.cost)}, ${multipleWords(spiked)}${driver}`;
}

/** How a meter's state reads: a rule that exists and is on is fine; anything else is unjudged, never red. */
export function meterTone(row: MeterRow): 'good' | 'info' {
  return row.state === 'exists' ? 'good' : 'info';
}

/** The meters section's own tone: fine when every rule exists and is on, unjudged while any waits. */
export function metersTone(rows: readonly MeterRow[] | undefined): 'good' | 'info' {
  return rows !== undefined && rows.length > 0 && rows.every((row) => meterTone(row) === 'good')
    ? 'good'
    : 'info';
}
