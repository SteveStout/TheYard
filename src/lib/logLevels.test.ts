import { describe, expect, it } from 'vitest';
import { levelLine } from './logLevels';

describe('levelLine', () => {
  it('counts every level present, worst first', () => {
    const entries = [
      { level: 'Information' },
      { level: 'Warning' },
      { level: 'Information' },
      { level: 'Error' },
      { level: 'Warning' },
    ];
    expect(levelLine(entries)).toBe('1 error, 2 warnings, 2 information');
  });

  it('says an empty window in words', () => {
    expect(levelLine([])).toBe('No lines in the window.');
  });

  it('puts a level it does not know after the ones it does', () => {
    expect(levelLine([{ level: 'Odd' }, { level: 'Critical' }])).toBe('1 critical, 1 odd');
  });
});
