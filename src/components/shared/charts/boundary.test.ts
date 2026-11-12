import { describe, expect, it } from 'vitest';

// Every component's source outside the Admin tab, read as text by the bundler the way icons.test.ts reads it.
const outside = import.meta.glob(
  [
    '../../**/*.ts',
    '../../**/*.tsx',
    '!../../admin/**',
    '!../../**/*.test.ts',
    '!../../**/*.test.tsx',
  ],
  { query: '?raw', import: 'default', eager: true }
) as Record<string, string>;

/** An import of anything under admin/: static or dynamic, in either quote (CodeRabbit on #57). */
const FROM_ADMIN = /(?:\bfrom\s*|\bimport\s*\(\s*)['"][^'"]*\/admin\//;

describe('the chart parts every page draws with live in shared (CodeRabbit on #8)', () => {
  it('knows an import of the Admin tab in every spelling', () => {
    for (const line of [
      "import { Chart } from '../../admin/Chart';",
      'import { Chart } from "../../admin/Chart";',
      "const Chart = lazy(() => import('../../admin/Chart'));",
      'export { Chart } from "../admin/Chart";',
    ]) {
      expect(FROM_ADMIN.test(line), line).toBe(true);
    }
    expect(FROM_ADMIN.test("import { Chart } from '../shared/charts/Chart';")).toBe(false);
  });

  it('no component outside the Admin tab imports from the Admin tab', () => {
    const reaching = Object.entries(outside)
      .filter(([, text]) => FROM_ADMIN.test(text))
      .map(([path]) => path);
    expect(Object.keys(outside).length).toBeGreaterThan(10);
    expect(reaching).toEqual([]);
  });
});
