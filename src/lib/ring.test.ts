import { describe, expect, it } from 'vitest';
import { RING, ringArc } from './ring';

describe("a ring gauge (ADR: The glass look, the addendum on the operator's look)", () => {
  it('draws the share of the whole, clockwise from the top, clear of its own stroke', () => {
    const arc = ringArc(5, 5, 44, 5);
    expect(arc.radius).toBe(19.5);
    expect(arc.share).toBe(1);
    expect(arc.drawn).toBe(arc.length);
    expect(ringArc(1, 4, 44, 5).drawn).toBeCloseTo(arc.length / 4, 1);
  });

  it('draws nothing where there is no whole or no number, and never past the whole', () => {
    expect(ringArc(3, 0, 44, 5).drawn).toBe(0);
    expect(ringArc(Number.NaN, 10, 44, 5).drawn).toBe(0);
    expect(ringArc(-2, 10, 44, 5).share).toBe(0);
    expect(ringArc(15, 10, 44, 5).share).toBe(1);
  });

  it('has one table of sizes, each stroke inside its box', () => {
    for (const { size, stroke } of Object.values(RING)) {
      expect(stroke).toBeLessThan(size / 4);
      expect(ringArc(1, 1, size, stroke).radius).toBeGreaterThan(0);
    }
  });
});
