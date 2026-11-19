import { describe, expect, it } from 'vitest';
import budgets from '../../.github/byte-budgets.json';
import history from '../../data/quality-history.json';

// It lives outside src because a file under src may import only what the image's frontend stage copies
// (DockerBuildInputsTests), and this one reads the build's output and the budget file.

// Ladder 8a (ADR-134): what a reader's first load costs, held to the bytes measured when the budgets were set. A public
// page's first load is the drawn index.html, the entry script and stylesheet it names, and, for a document, the
// markdown chunk. Admin's chunks are not a reader's first load and are not counted. The gate's node job builds the
// site before Vitest runs, so on a gate branch the build is always read; CI on main runs Vitest before its build, and
// a machine that has not built says so and is not judged.
const files = import.meta.glob(
  ['../../dist/index.html', '../../dist/assets/*.js', '../../dist/assets/*.css'],
  {
    query: '?raw',
    import: 'default',
    eager: true,
  }
) as Record<string, string>;
const bytes = (text: string) => new TextEncoder().encode(text).length;
const named = (name: string) => files[`../../dist${name}`];
const html = files['../../dist/index.html'];

function firstLoad(): Record<string, number> {
  const markdown = Object.keys(files).find((path) => /\/markdown-[^/]*\.js$/.test(path))!;
  return {
    index_html: bytes(html),
    entry_js: bytes(named(html.match(/<script[^>]+src="(\/assets\/index-[^"]+\.js)"/)![1])),
    entry_css: bytes(named(html.match(/<link[^>]+href="(\/assets\/index-[^"]+\.css)"/)![1])),
    markdown_js: bytes(files[markdown]),
  };
}

/**
 * The most bytes <part> may take: its number, plus its allowance for each history row past the rows there were when
 * the number was measured, since the landing draws every reading and promote adds one after each version.
 */
export function allowed(
  part: string,
  file: {
    bytes: Record<string, number>;
    history_rows: number;
    per_history_row: Record<string, number>;
  },
  rows: number
): number {
  return (
    file.bytes[part] + (file.per_history_row[part] ?? 0) * Math.max(0, rows - file.history_rows)
  );
}

/**
 * Whether this run is a gate's: a lane pushes ready/<lane>-<sha8> (ADR-137), or gate/<version> before the cop, and only
 * the gate's node job builds before Vitest.
 */
const onTheGate = (ref: string | undefined) =>
  ref?.startsWith('refs/heads/ready/') === true || ref?.startsWith('refs/heads/gate/') === true;

describe('the first load stays inside its byte budget', () => {
  it('runs on the gate, where the site is always built first', () => {
    if (onTheGate(import.meta.env.GITHUB_REF))
      expect(html, 'the node job builds before Vitest').toBeDefined();
  });

  it('asks for the build only on a gate branch, not in CI on main', () => {
    expect(onTheGate('refs/heads/gate/1.0.5.64')).toBe(true);
    expect(onTheGate('refs/heads/ready/code1118e-1a2b3c4d')).toBe(true);
    expect(onTheGate('refs/heads/main')).toBe(false);
    expect(onTheGate(undefined)).toBe(false);
  });

  it("lets the two parts that draw the history grow by one row's allowance a row, and the others not at all", () => {
    const file = {
      bytes: { index_html: 100, entry_css: 50 },
      history_rows: 10,
      per_history_row: { index_html: 4 },
    };
    expect(allowed('index_html', file, 10)).toBe(100);
    expect(allowed('index_html', file, 13)).toBe(112);
    expect(allowed('index_html', file, 8)).toBe(100);
    expect(allowed('entry_css', file, 13)).toBe(50);
  });

  it.skipIf(html === undefined)('spends no more bytes than the budget names, part by part', () => {
    for (const [part, measured] of Object.entries(firstLoad())) {
      expect((budgets.bytes as Record<string, number>)[part], `${part} has no budget`).toBeTypeOf(
        'number'
      );
      const budget = allowed(part, budgets, history.rows.length);
      expect(
        measured,
        `${part}: ${measured} bytes against a budget of ${budget}`
      ).toBeLessThanOrEqual(budget);
    }
  });
});
