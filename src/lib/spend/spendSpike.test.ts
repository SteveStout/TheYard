import { describe, expect, it } from 'vitest';
import type { MeterRow, SpikeDay } from './costReport';
import {
  meterTone,
  metersTone,
  multipleWords,
  spikeBars,
  spikeRuleWords,
  spikeSentence,
} from './spendSpike';

const day = (
  date: string,
  cost: number,
  median: number | null,
  extra: Partial<SpikeDay> = {}
): SpikeDay => ({
  day: date,
  cost,
  partial: false,
  median,
  days: median === null ? 0 : 7,
  multiple: median === null ? null : Math.round((cost / median) * 10) / 10,
  biggest: 'stjobhuntss',
  biggest_cost: cost,
  ...extra,
});

const meter = (state: string): MeterRow => ({
  meter: 'Blob egress',
  watches: "the job-hunt account's blobs",
  rule: 'alert-jobhunt-blob-egress',
  state,
  line: 'Egress over 2,000,000,000 in 1 hour',
});

describe('the spike guard and the meters (ADR: What Azure charges, the spike guard)', () => {
  it('draws the days oldest first, scaled to the tallest bar or line, each toned by its own line', () => {
    const bars = spikeBars([
      day('2026-10-23', 25.74, 1.47),
      day('2026-10-22', 13.41, 1.45),
      day('2026-10-21', 4.4, 1.45),
    ]);

    expect(bars.map((bar) => bar.label)).toEqual(['21 Oct', '22 Oct', '23 Oct']);
    expect(bars.map((bar) => bar.tone)).toEqual(['good', 'bad', 'bad']);
    expect(bars[2].height).toBe(1);
    expect(bars[0].line).toBe(5);
    expect(bars[0].lineAt).toBeCloseTo(5 / 25.74, 5);
    expect(spikeBars([])).toEqual([]);
  });

  it('says the rule from the same constants the tone reads', () => {
    expect(spikeRuleWords()).toBe(
      'red on any day over 3x the median of the week before it (never under $5.00)'
    );
  });

  it('names a spike on the newest two days with its multiple and its driver, and nothing older', () => {
    expect(
      spikeSentence([
        day('2026-10-24', 0.4, 1.5, { partial: true }),
        day('2026-10-23', 25.74, 1.47),
      ])
    ).toBe("spike: 23 Oct $25.74, 17.5x the week's median $1.47, driven by stjobhuntss");
    expect(
      spikeSentence([
        day('2026-11-04', 1.4, 1.45),
        day('2026-11-03', 1.59, 1.4),
        day('2026-10-23', 25.74, 1.47),
      ])
    ).toBeNull();
    expect(spikeSentence(undefined)).toBeNull();
    expect(multipleWords(day('2026-11-01', 1.33, null))).toBe('no finished week before it yet');
    expect(multipleWords(day('2026-11-02', 1.38, 1.33, { days: 1, multiple: 1 }))).toBe(
      "1x 1 day's median $1.33"
    );
    expect(multipleWords(day('2026-11-03', 1.59, 1.36, { days: 2, multiple: 1.2 }))).toBe(
      "1.2x 2 days' median $1.36"
    );
  });

  it('reads a rule that exists as fine and every other state as unjudged, never red', () => {
    expect(meterTone(meter('exists'))).toBe('good');
    expect(meterTone(meter('not set'))).toBe('info');
    expect(meterTone(meter('off'))).toBe('info');
    expect(metersTone([meter('exists'), meter('exists')])).toBe('good');
    expect(metersTone([meter('exists'), meter('not read yet')])).toBe('info');
    expect(metersTone(undefined)).toBe('info');
    expect(metersTone([])).toBe('info');
  });
});
