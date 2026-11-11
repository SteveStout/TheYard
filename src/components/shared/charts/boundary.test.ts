import { describe, expect, it } from 'vitest';

// Every component's source outside the Admin tab, read as text by the bundler the way icons.test.ts reads it.
const outside = import.meta.glob(
  ['../../**/*.ts', '../../**/*.tsx', '!../../admin/**', '!../../**/*.test.ts', '!../../**/*.test.tsx'],
  { query: '?raw', import: 'default', eager: true }
) as Record<string, string>;

describe('the chart parts every page draws with live in shared (CodeRabbit on #8)', () => {
  it('no component outside the Admin tab imports from the Admin tab', () => {
    const reaching = Object.entries(outside)
      .filter(([, text]) => /from '[^']*\/admin\//.test(text))
      .map(([path]) => path);
    expect(Object.keys(outside).length).toBeGreaterThan(10);
    expect(reaching).toEqual([]);
  });
});
