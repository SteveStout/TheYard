import { describe, expect, it } from 'vitest';
import { COMET, cometFrames } from './comet';

describe('the comet on the quality line (sales1120)', () => {
  it('runs 4.5 seconds a run, three runs', () => {
    expect(COMET.ms).toBe(4500);
    expect(COMET.runs).toBe(3);
  });

  it('lays the line’s own points over the stretched chart, fading in after the first and out at the last', () => {
    const frames = cometFrames(
      [
        { x: 0, y: 40 },
        { x: 100, y: 20 },
        { x: 200, y: 40 },
      ],
      { width: 200, height: 80 },
      { left: 120, top: 60, width: 400, height: 84 },
      { left: 100, top: 20 }
    );
    expect(frames).toEqual([
      { left: '20px', top: '82px', opacity: 0 },
      { left: '220px', top: '61px', opacity: 1 },
      { left: '420px', top: '82px', opacity: 0 },
    ]);
  });
});
