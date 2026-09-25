import { describe, expect, it } from 'vitest';
import { RING, ringArc, ringGraduations, ringMarker } from './ring';

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

  // The tweaks pass (A3, B2): a ring at 100 per cent reads as a gauge that came round.
  it('puts the marker at the end of the fill, at twelve o clock when it is full, and nowhere when empty', () => {
    expect(ringMarker(0, 44, 5)).toBeNull();
    expect(ringMarker(1, 44, 5)).toEqual({ x: 22, y: 2.5 });
    expect(ringMarker(0.25, 44, 5)).toEqual({ x: 41.5, y: 22 });
    expect(ringMarker(0.5, 44, 5)).toEqual({ x: 22, y: 41.5 });
  });

  it('graduates a large ring with thirty-six ticks outside its stroke, the quarters twice as long', () => {
    const ticks = ringGraduations(200);
    expect(ticks).toHaveLength(36);
    expect(ticks.filter((tick) => tick.major)).toHaveLength(4);
    expect(ticks[0]).toEqual({ x1: 100, y1: -3, x2: 100, y2: -9, major: true });
    expect(ticks[1].y1 - ticks[1].y2).toBeCloseTo(3 * Math.cos((2 * Math.PI) / 36), 1);
  });
});
