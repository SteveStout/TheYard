import { describe, expect, it } from 'vitest';
import { countable, easeOut, figureAt } from './countUp';

describe("a reading that eases up to its value (the operator's look)", () => {
  it('reads the number a figure starts with and keeps what follows it', () => {
    expect(countable('1,234')).toEqual({ value: 1234, suffix: '' });
    expect(countable('326 s')).toEqual({ value: 326, suffix: ' s' });
    expect(countable('under 1 ms')).toBeNull();
    expect(countable('')).toBeNull();
  });

  it('starts at nothing, ends at the figure written as the figure is, and never passes it', () => {
    const figure = countable('1,515')!;
    expect(figureAt(figure, 0)).toBe('0');
    expect(figureAt(figure, 1)).toBe('1,515');
    expect(figureAt(figure, 2)).toBe('1,515');
    expect(easeOut(0.5)).toBeGreaterThan(0.5);
    for (let t = 0; t <= 1; t += 0.1) expect(easeOut(t)).toBeLessThanOrEqual(1);
  });
});
