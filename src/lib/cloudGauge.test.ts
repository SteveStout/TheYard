import { describe, expect, it } from 'vitest';
import {
  GAUGE_SWEEP,
  LABEL_GAP,
  gaugeAngle,
  gaugeArc,
  gaugeLabels,
  holdLine,
  sparksFor,
} from './cloudGauge';

describe('the dial as the approved canvas draws it', () => {
  it('opens at the bottom: from seven o’clock round three quarters to five', () => {
    expect(gaugeAngle(0)).toBe(135);
    expect(gaugeAngle(100)).toBe(135 + GAUGE_SWEEP);
    expect(gaugeAngle(150)).toBe(405);
    expect(gaugeAngle(-5)).toBe(135);
    // Past half the ring the arc is the large one.
    expect(gaugeArc(100, 100, 50, 0, 75)).toContain(' 0 1 1 ');
    expect(gaugeArc(100, 100, 50, 0, 20)).toContain(' 0 0 1 ');
  });

  it('names each threshold on the dial, on the side its tick is on, with no row of chips', () => {
    const labels = gaugeLabels({ amber: 60, red: 80, p95: 18 }, 170, 100, 64, 190);
    expect(labels.map((l) => l.text)).toEqual(['amber 60 %', 'red 80 %', 'p95 18 %']);
    // Amber at sixty and red at eighty sit on the right half of the ring; the 95th at eighteen on the left.
    expect(labels.map((l) => l.anchor)).toEqual(['start', 'start', 'end']);
    expect(labels[2].textX).toBeLessThan(170 - 64);
    expect(labels[0].textX).toBeGreaterThan(170 + 64);
    for (const label of labels) expect(label.leader.startsWith('M ')).toBe(true);
  });

  it('pushes two labels on one side apart, so amber 70 and the 95th at 75 never touch', () => {
    const labels = gaugeLabels({ amber: 70, red: 85, p95: 75 }, 170, 100, 64, 190);
    const right = labels
      .filter((l) => l.anchor === 'start')
      .map((l) => l.textY)
      .sort((a, b) => a - b);
    expect(right).toHaveLength(3);
    for (let i = 1; i < right.length; i += 1)
      expect(right[i] - right[i - 1]).toBeGreaterThanOrEqual(LABEL_GAP);
    for (const y of right) expect(y).toBeLessThanOrEqual(190);
  });

  it('prints a level past the ring’s end as it is, with its tick pinned to the end', () => {
    const labels = gaugeLabels({ amber: 200, red: 400, p95: null }, 170, 100, 64, 190);
    expect(labels.map((l) => l.text)).toEqual(['amber 200 %', 'red 400 %']);
    expect(Math.abs(labels[1].textY - labels[0].textY)).toBeGreaterThanOrEqual(LABEL_GAP);
  });

  it('says how a dial’s colour is held in one quiet line, and nothing when it is not held', () => {
    expect(
      holdLine([
        { label: 'CPU amber 60 %', tone: 'amber' },
        { label: 'held 15 min', tone: 'held' },
      ])
    ).toBe('a dial changes colour only after 15 min past its line');
    expect(holdLine([{ label: 'CPU amber 60 %', tone: 'amber' }])).toBe('');
  });
});

describe('which hour sits under which dial', () => {
  const spark = (name: string) => ({ name, values: [1, 2] });

  it('puts a dial’s own sparkline under it, and keeps every other one for the row below', () => {
    const cosmos = sparksFor(
      [{ name: 'RU/s, right now' }, { name: 'Storage' }],
      [spark('RU/s, 60 min'), spark('Normalized RU, 60 min')]
    );
    expect(cosmos.byDial.map((s) => s?.name)).toEqual(['RU/s, 60 min', undefined]);
    expect(cosmos.rest.map((s) => s.name)).toEqual(['Normalized RU, 60 min']);
  });

  it('drops no sparkline when a card sends three', () => {
    const share = sparksFor(
      [{ name: 'Transactions this hour' }, { name: 'Used of the quota' }],
      [spark('Listings, 60 min'), spark('Reads, 60 min'), spark('Writes, 60 min')]
    );
    expect(share.byDial).toEqual([undefined, undefined]);
    expect(share.rest).toHaveLength(3);
  });

  it('matches a dial named by more words than its sparkline', () => {
    const plan = sparksFor(
      [{ name: 'CPU, right now' }, { name: 'Sampling kept' }],
      [spark('CPU, 60 min'), spark('Sampling, 60 h')]
    );
    expect(plan.byDial.map((s) => s?.name)).toEqual(['CPU, 60 min', 'Sampling, 60 h']);
    expect(plan.rest).toEqual([]);
  });
});
