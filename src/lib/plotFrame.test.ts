import { describe, expect, it } from 'vitest';
import { FRAME, plotFrame } from './plotFrame';

const box = { width: 200, height: 100, top: 10, right: 10, bottom: 20, left: 30 };

describe('plotFrame', () => {
  it('graduates the side at the quarters, major at nothing, half and the ceiling, all outside the plot', () => {
    const up = plotFrame(box, []).ticks.filter((tick) => tick.key.startsWith('y'));
    expect(up.map((tick) => tick.y1)).toEqual([80, 62.5, 45, 27.5, 10]);
    expect(up.map((tick) => tick.major)).toEqual([true, false, true, false, true]);
    for (const tick of up) {
      expect(tick.x2).toBe(box.left);
      expect(tick.x2 - tick.x1).toBe(tick.major ? FRAME.major : FRAME.minor);
    }
  });

  it('draws a minor tick under a major one once, as the major', () => {
    const { ticks } = plotFrame(box, [
      { key: 'm0', x: 30, major: false },
      { key: 'm1', x: 110, major: false },
      { key: 'x0', x: 30.02, major: true },
    ]);
    const bottom = ticks.filter((tick) => !tick.key.startsWith('y'));
    expect(bottom.map((tick) => tick.key)).toEqual(['m1', 'x0']);
    for (const tick of bottom) expect(tick.y1).toBe(box.height - box.bottom);
    expect(bottom.find((tick) => tick.major)?.y2).toBe(box.height - box.bottom + FRAME.major);
  });

  it('puts one bracket in the top left corner and one in the bottom right, inside the plot', () => {
    expect(plotFrame(box, []).brackets).toEqual(['M31 19V11H39', 'M181 79H189V71']);
  });
});
