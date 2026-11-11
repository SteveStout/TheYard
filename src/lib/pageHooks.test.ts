import { describe, expect, it } from 'vitest';
import file from '../../data/hooks.json';
import history from '../../data/quality-history.json';
import { hookFor, hookTrend, opensAdmin, withCounts, HOOK_PAGES } from './pageHooks';

// #region one-file
/**
 * The hook on every page (ADR-132, A hook on every page): one entry a page in data/hooks.json, read by this build
 * and by the API for /about and the resume door (PageHooksTests holds the API's side to the same file, and ties the
 * sentence's first word to the ratchet's count and the chart's label to the Quality page, which live outside what the
 * image's frontend build copies). These hold the file to its copy rules and every address in it away from Admin.
 */
const WORDS = [
  'zero',
  'one',
  'two',
  'three',
  'four',
  'five',
  'six',
  'seven',
  'eight',
  'nine',
  'ten',
  'eleven',
  'twelve',
  'thirteen',
  'fourteen',
  'fifteen',
  'sixteen',
  'seventeen',
  'eighteen',
  'nineteen',
  'twenty',
];

describe('the hook data, one entry a page', () => {
  it('has one entry for each page a reader is sent to, and no other', () => {
    expect(file.hooks.map((hook) => hook.page).sort()).toEqual([...HOOK_PAGES].sort());
    for (const page of HOOK_PAGES) expect(hookFor(page).page).toBe(page);
  });

  it('says the quality sentence the same on the landing and the resume door, word for word as Steve wrote it', () => {
    const said = file.hooks
      .flatMap((hook) => hook.lines)
      .filter((line) => /quality numbers/.test(line));
    expect(said).toEqual([
      'Fifteen quality numbers are measured on every version and none may get worse.',
      'Fifteen quality numbers are measured on every version and none may get worse.',
    ]);
    expect(hookFor('landing').lines).toEqual(hookFor('resume').lines);
  });

  it('keeps the copy rules: no em dash, never the two job words, and a number in what each page says', () => {
    const text = JSON.stringify(file.hooks);
    expect(text).not.toMatch(/\u2014/);
    expect(text).not.toMatch(/hiring manager|recruiter/i);
    expect(text).not.toMatch(/zero downtime|no downtime/i);
    for (const hook of file.hooks.filter((each) => each.lines.length > 0)) {
      expect(hook.lines.join(' '), hook.page).toMatch(
        new RegExp(`\\d|\\b(${WORDS.slice(2).join('|')})\\b`, 'i')
      );
    }
  });

  it('types no count it can read: the resume door names its recommendations by a placeholder the data fills', () => {
    const resume = hookFor('resume');
    expect(resume.link?.label).toMatch(/\{recommendations\}/);
    expect(resume.link?.label).not.toMatch(/\d/);
    expect(withCounts('Read the {recommendations} recommendations', { recommendations: 13 })).toBe(
      'Read the 13 recommendations'
    );
  });

  it('draws the Quality page first chart with every reading from the history the build carries', () => {
    const landing = hookFor('landing');
    const trend = hookTrend(landing);
    expect(trend).not.toBeNull();
    const measured = history.rows.filter(
      (row) => typeof (row.numbers as Record<string, unknown>)[landing.chart!.number] === 'number'
    );
    expect(trend!.points.length).toBe(measured.length);
    expect(trend!.marker).toBe(history.first_ratchet_version);
    expect(trend!.points.at(-1)?.version).toBe(measured.at(-1)?.version);
  });
});
// #endregion one-file

// #region never-admin
describe('no hook opens Admin', () => {
  it('knows an Admin address when it sees one', () => {
    expect(opensAdmin('/?view=admin')).toBe(true);
    expect(opensAdmin('/?view=admin&card=activity')).toBe(true);
    expect(opensAdmin('https://theyard.stevenstout.biz/?doc=quality&view=admin')).toBe(true);
    expect(opensAdmin('/?doc=quality')).toBe(false);
    expect(opensAdmin('/recommendations')).toBe(false);
  });

  it('holds every address in the hook data away from Admin', () => {
    const addresses = file.hooks.flatMap((hook) => [hook.path, hook.link?.href ?? '']);
    expect(addresses.filter((href) => href !== '' && opensAdmin(href))).toEqual([]);
    expect(JSON.stringify(file)).not.toMatch(/view=admin|\/api\/admin/);
  });
});
// #endregion never-admin
