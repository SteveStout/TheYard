import { describe, expect, it } from 'vitest';
import { format, parseTrend, TREND_BOX, trendGeometry } from './qualityTrend';

const fence = [
  'Async methods with no CancellationToken | may only fall | 1.0.4.115',
  '2026-10-15 |  | 50 | pre-scan',
  '2026-10-22 |  | 104 | pre-scan',
  '2026-10-29 | 1.0.4.115 | 113 | quality.py',
  '2026-11-10 | 1.0.4.178 | 1 | quality.py',
  'a line it cannot read',
].join('\n');

describe('the quality trend', () => {
  it('reads the fence the API fills: the head, then a reading a line, an unreadable line left out', () => {
    const data = parseTrend(fence);
    expect(data.label).toBe('Async methods with no CancellationToken');
    expect(data.direction).toBe('may only fall');
    expect(data.marker).toBe('1.0.4.115');
    expect(data.points.map((p) => p.value)).toEqual([50, 104, 113, 1]);
    expect(data.points.map((p) => p.preScan)).toEqual([true, true, false, false]);
  });

  it('sits the versions evenly, dashes the pre-scan, and marks the version the record names', () => {
    const shape = trendGeometry(parseTrend(fence));
    const xs = shape.points.map((p) => p.x);
    expect(xs[0]).toBe(TREND_BOX.left);
    expect(xs[3]).toBe(TREND_BOX.width - TREND_BOX.right);
    expect(xs[2] - xs[1]).toBeCloseTo(xs[1] - xs[0], 1);
    expect(shape.preScan.split(' L').length).toBe(2);
    expect(shape.measured.startsWith(`M${xs[1]} `)).toBe(true);
    expect(shape.marker?.x).toBe(xs[2]);
    expect(shape.points[2].y).toBeLessThan(shape.points[3].y);
    expect(shape.xLabels.map((l) => l.text)).toEqual(['10-15', '1.0.4.178']);
  });

  it('writes whole numbers whole and ratios to two places', () => {
    expect(format(399)).toBe('399');
    expect(format(0.8)).toBe('0.80');
  });
});
