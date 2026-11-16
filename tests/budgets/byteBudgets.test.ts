import { describe, expect, it } from 'vitest';
import budgets from '../../.github/byte-budgets.json';

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

/** Whether this run is a gate's: the gate pushes gate/<version>, and only its node job builds before Vitest. */
const onTheGate = (ref: string | undefined) => ref?.startsWith('refs/heads/gate/') === true;

describe('the first load stays inside its byte budget', () => {
  it('runs on the gate, where the site is always built first', () => {
    if (onTheGate(import.meta.env.GITHUB_REF))
      expect(html, 'the node job builds before Vitest').toBeDefined();
  });

  it('asks for the build only on a gate branch, not in CI on main', () => {
    expect(onTheGate('refs/heads/gate/1.0.5.64')).toBe(true);
    expect(onTheGate('refs/heads/main')).toBe(false);
    expect(onTheGate(undefined)).toBe(false);
  });

  it.skipIf(html === undefined)('spends no more bytes than the budget names, part by part', () => {
    for (const [part, measured] of Object.entries(firstLoad())) {
      const budget = (budgets.bytes as Record<string, number>)[part];
      expect(budget, `${part} has no budget`).toBeTypeOf('number');
      expect(
        measured,
        `${part}: ${measured} bytes against a budget of ${budget}`
      ).toBeLessThanOrEqual(budget);
    }
  });
});
