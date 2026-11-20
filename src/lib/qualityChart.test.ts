import { describe, expect, it } from 'vitest';
import {
  chartGeometry,
  floorOf,
  roundUp,
  scaleOf,
  shortDay,
  SLIP_WIDE,
  SMALL,
} from './qualityChart';
import { parseTrend } from './qualityTrend';

const slip = parseTrend(
  [
    'Async methods with no CancellationToken | may only fall | 1.0.4.115 | slip',
    '2026-10-15 | 1.0.3.139 | 51 | quality.py',
    '2026-10-17 | 1.0.3.186 | 109 | quality.py',
    '2026-10-29 | 1.0.4.114 | 108 | quality.py',
    '2026-10-29 | 1.0.4.115 | 107 | quality.py',
    '2026-11-20 | 1.0.5.122 | 1 | quality.py',
  ].join('\n')
);

describe('the Quality page charts (the quality1120 picture)', () => {
  it('spaces readings by their day, so one day is one x', () => {
    const shape = chartGeometry(slip, SLIP_WIDE);
    const plot = SLIP_WIDE.width - SLIP_WIDE.left - SLIP_WIDE.right;
    expect(shape.xLabels.map((label) => label.text)).toEqual(['Oct 15', 'Oct 29', 'Nov 20']);
    expect(shape.join?.x).toBeCloseTo(SLIP_WIDE.left + (plot * 14) / 36, 0);
    expect(shape.join?.words).toBe('1.0.4.115, Oct 29');
  });

  it('draws the slip to the ratchet and three callouts: the first reading, the worst and today', () => {
    const shape = chartGeometry(slip, SLIP_WIDE);
    expect(shape.slip?.words).toBe('THE SLIP, OCT 15 TO 29');
    expect(shape.slip?.right).toBe(shape.join?.x);
    expect(shape.callouts.map((c) => `${c.value} ${c.words}`)).toEqual([
      '51 Oct 15, first reading',
      '109 the worst, 1.0.3.186',
      '1 today, 1.0.5.122',
    ]);
    expect(chartGeometry(slip, SLIP_WIDE, true).slip?.words).toBe('THE SLIP');
  });

  it('labels round gridlines, and a small chart has a dot at today and no callouts', () => {
    expect(scaleOf(slip).ticks).toEqual([0, 25, 50, 75, 100]);
    const small = chartGeometry({ ...slip, form: 'small' }, SMALL);
    expect(small.grid.map((tick) => tick.text)).toEqual(['0', '60', '120']);
    expect(small.callouts).toEqual([]);
    expect(small.end).not.toBeNull();
    expect(small.slip).toBeNull();
  });

  it('draws a floor where the fence names one, the scale around the floor', () => {
    const coverage = parseTrend(
      'Line coverage, per cent | floor 90 | | small\n2026-10-15 | 1.0.3.139 | 91.4 | ci\n2026-11-20 | 1.0.5.122 | 94.2 | ci'
    );
    const shape = chartGeometry(coverage, SMALL);
    expect(floorOf(coverage.direction)).toBe(90);
    expect(shape.floor?.words).toBe('FLOOR 90');
    expect(shape.join).toBeNull();
    expect(scaleOf(coverage)).toEqual({ low: 85, high: 98, ticks: [85, 92, 98] });
  });

  it('rounds a top up to a number a reader expects, and writes a day short', () => {
    expect(roundUp(117.7)).toBe(120);
    expect(roundUp(593)).toBe(600);
    expect(roundUp(0.96)).toBe(1);
    expect(shortDay('2026-11-20')).toBe('Nov 20');
  });

  it("calls a rising number's lowest reading its worst, and names a slip across a month by both months", () => {
    const rising = parseTrend(
      [
        'xUnit facts and data rows | may only rise | 1.0.4.115 | slip',
        '2026-10-25 | 1.0.4.90 | 950 | quality.py',
        '2026-10-28 | 1.0.4.100 | 900 | quality.py',
        '2026-11-02 | 1.0.4.115 | 2255 | quality.py',
        '2026-11-20 | 1.0.5.122 | 4555 | quality.py',
      ].join('\n')
    );
    const shape = chartGeometry(rising, SLIP_WIDE);
    expect(shape.callouts[1].words).toBe('the worst, 1.0.4.100');
    expect(shape.callouts[1].value).toBe('900');
    expect(shape.callouts[2].value).toBe('4,555');
    expect(shape.slip?.words).toBe('THE SLIP, OCT 25 TO NOV 2');
  });

  it('dashes the pre-scan, and the solid line starts at the scanner', () => {
    const shape = chartGeometry(
      parseTrend(
        'Declarations | may only fall | 1.0.4.115 | small\n2026-10-10 |  | 200 | pre-scan\n2026-10-15 | 1.0.3.139 | 240 | quality.py\n2026-10-29 | 1.0.4.115 | 549 | quality.py'
      ),
      SMALL
    );
    expect(shape.preScan.split(' L')).toHaveLength(2);
    expect(shape.line.startsWith('M')).toBe(true);
    expect(shape.line.split(' L')).toHaveLength(2);
    expect(shape.summary).toContain('549 on Oct 29 at its worst');
  });
});
