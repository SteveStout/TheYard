import { describe, expect, it } from 'vitest';
import { followsWords, roundTop, scatter, trendOf, type FollowsPoint } from './follows';

const point = (rpm: number, cpu: number | null, mb: number, minute = 0): FollowsPoint => ({
  at: `2026-10-19T00:${String(minute).padStart(2, '0')}:00Z`,
  requests_per_minute: rpm,
  cpu_percent: cpu,
  working_set_mb: mb,
});

describe('memory against traffic (ADR: Memory follows traffic)', () => {
  it('rounds an axis up to a number a reader can read', () => {
    expect(roundTop(0)).toBe(1);
    expect(roundTop(7)).toBe(10);
    expect(roundTop(43)).toBe(50);
    expect(roundTop(120)).toBe(200);
    expect(roundTop(230)).toBe(250);
  });

  it('places requests along the bottom and leaves out a reading that was not taken', () => {
    const plot = scatter(
      [point(10, 2, 400, 1), point(40, null, 410, 2), point(20, 4, 405, 3)],
      (p) => p.cpu_percent,
      100
    );
    expect(plot.xMax).toBe(20);
    expect(plot.yMax).toBe(100);
    expect(plot.dots).toHaveLength(2);
    expect(plot.dots[0]).toMatchObject({ x: 0.5, y: 0.02, requests: 10, value: 2 });
  });

  it('draws a rising line when the processor follows traffic and a flat one when memory does not', () => {
    const points = [1, 2, 3, 4, 5, 6].map((i) => point(i * 10, i * 3, 500, i));
    const cpu = scatter(points, (p) => p.cpu_percent, 100);
    const memory = scatter(points, (p) => p.working_set_mb, 1000);
    expect(cpu.trend!.y2).toBeGreaterThan(cpu.trend!.y1);
    expect(memory.trend!.y2).toBeCloseTo(memory.trend!.y1, 6);
  });

  it('has no line when traffic never moved', () => {
    expect(
      trendOf([
        { x: 0.5, y: 0.1, requests: 5, value: 1, at: '' },
        { x: 0.5, y: 0.3, requests: 5, value: 3, at: '' },
      ])
    ).toBeNull();
  });

  it('says how closely a reading followed traffic', () => {
    expect(followsWords(0.92)).toBe('followed it closely (r = 0.92)');
    expect(followsWords(0.1)).toBe('followed it hardly (r = 0.10)');
    expect(followsWords(-0.6)).toBe('moved against it (r = -0.60)');
    expect(followsWords(null)).toBe('did not move enough to compare');
  });
});
