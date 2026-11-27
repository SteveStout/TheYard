import { describe, expect, it } from 'vitest';
import {
  NO_FILTER,
  emptyLine,
  labelsOf,
  filterFromSearch,
  filterToParams,
  isFiltered,
  isDrawnFor,
  listPath,
  marks,
  matchLine,
  minutesLine,
  slideFrom,
} from './blogFilter';

describe('the blog filter in the address', () => {
  it('reads every part it knows and leaves out what it does not', () => {
    expect(
      filterFromSearch(
        '?view=blog&q=onion&topic=architecture&keyword=cqrs&month=2026-11&sort=read&r=ab12cd'
      )
    ).toEqual({
      q: 'onion',
      topic: 'architecture',
      keyword: 'cqrs',
      month: '2026-11',
      sort: 'read',
    });
    expect(filterFromSearch('?view=blog&topic=Not%20A%20Key&month=nov&sort=sideways')).toEqual(
      NO_FILTER
    );
    // A topic or keyword typed in capitals is the same chip (the exploratory pass of findable1120).
    expect(filterFromSearch('?view=blog&topic=AI&keyword=cLaUdE')).toEqual({
      ...NO_FILTER,
      topic: 'ai',
      keyword: 'claude',
    });
    expect(filterFromSearch('?view=blog&topic=Job-Search').topic).toBe('job-search');
    expect(filterFromSearch('?view=blog&year=2026')).toEqual({
      ...NO_FILTER,
      month: null,
      year: 2026,
    });
  });

  it('writes only the parts that are set, in one order, so one view has one address', () => {
    const params = filterToParams({
      q: ' onion ',
      topic: 'architecture',
      keyword: null,
      month: null,
      sort: 'new',
    });
    expect(params.toString()).toBe('topic=architecture&q=onion');
    expect(filterToParams(NO_FILTER).toString()).toBe('');
    expect(filterToParams({ ...NO_FILTER, sort: 'read', month: '2026-10' }).toString()).toBe(
      'month=2026-10&sort=read'
    );
    expect(
      filterFromSearch(
        `?view=blog&${filterToParams({ q: 'ms', topic: 'ai', keyword: 'copilot', month: '2026-11', sort: 'read' })}`
      )
    ).toEqual({ q: 'ms', topic: 'ai', keyword: 'copilot', month: '2026-11', sort: 'read' });
  });

  it('asks the API for the same filter, a page at a time', () => {
    expect(listPath({ ...NO_FILTER, topic: 'ai' })).toBe('/api/blog/posts?topic=ai');
    expect(listPath(NO_FILTER, 'n.0.5.3')).toBe('/api/blog/posts?after=n.0.5.3');
    expect(isFiltered(NO_FILTER)).toBe(false);
    expect(isFiltered({ ...NO_FILTER, q: '  ' })).toBe(false);
    expect(isFiltered({ ...NO_FILTER, sort: 'read' })).toBe(true);
  });

  it('takes the list the server drew only for the filter it was drawn for', () => {
    expect(isDrawnFor('?view=blog&topic=ai', { ...NO_FILTER, topic: 'ai' })).toBe(true);
    expect(isDrawnFor('?view=blog&post=a-post&q=copilot', NO_FILTER)).toBe(false);
    expect(isDrawnFor('?view=blog&post=a-post&slide=3', NO_FILTER)).toBe(true);
    expect(isDrawnFor(undefined, NO_FILTER)).toBe(false);
  });

  it('reads the slide a deep link opens on, and nothing that is not a slide', () => {
    expect(slideFrom('?view=blog&post=a-post&slide=3')).toBe(3);
    expect(slideFrom('?view=blog&post=a-post&slide=0')).toBeNull();
    expect(slideFrom('?view=blog&post=a-post&slide=x')).toBeNull();
    expect(slideFrom('?view=blog&slide=2')).toBeNull();
  });
});

describe('what a search shows', () => {
  it('says where a post matched, five places at most', () => {
    expect(matchLine([])).toBeNull();
    expect(matchLine(['Microsoft Copilot', '24 mentions in the text'])).toBe(
      'Matched on Microsoft Copilot · 24 mentions in the text'
    );
    expect(matchLine(['title', 'a', 'b', 'c', 'd', 'e'])).toBe('Matched on title · a · b · c · d');
  });

  it('marks each widened term where a word starts, the whole word, and never inside one', () => {
    const parts = marks('Microsoft Copilot and GitHub Copilot, not copilots in items', [
      'copilot',
      'microsoft copilot',
      'ms',
    ]);
    expect(parts.filter((p) => p.mark).map((p) => p.text)).toEqual([
      'Microsoft Copilot',
      'Copilot',
      'copilots',
    ]);
    expect(parts.map((p) => p.text).join('')).toBe(
      'Microsoft Copilot and GitHub Copilot, not copilots in items'
    );
    expect(marks('39 ms later', ['microsoft'])).toEqual([{ text: '39 ms later', mark: false }]);
    expect(marks('Plain text', [])).toEqual([{ text: 'Plain text', mark: false }]);
    expect(
      marks('Moving to .NET and C#', ['.net', 'c#'])
        .filter((p) => p.mark)
        .map((p) => p.text)
    ).toEqual(['.NET', 'C#']);
    expect(marks('a (b) [c]', ['(b', '[c']).every((p) => !p.mark)).toBe(true);
  });

  it('says what was asked when nothing matches', () => {
    const labels = {
      topics: { ai: 'AI' },
      keywords: { copilot: 'Microsoft Copilot' },
      months: { '2026-11': 'November 2026' },
    };
    expect(
      emptyLine(
        { q: 'kubernetes', topic: 'ai', keyword: 'copilot', month: '2026-11', sort: 'new' },
        labels
      )
    ).toBe('No post matches "kubernetes" in AI about Microsoft Copilot in November 2026.');
    expect(emptyLine({ ...NO_FILTER, topic: 'ai' }, labels)).toBe('No post matches in AI.');
    expect(
      emptyLine({ ...NO_FILTER, topic: 'linux', keyword: 'rust', month: '2025-01' }, labels)
    ).toBe('No post matches in linux about rust in 2025-01.');
    expect(emptyLine(NO_FILTER, labels)).toBe('No post matches.');
  });

  it('gives a post its reading time in whole minutes', () => {
    expect(minutesLine(1)).toBe('1 min read');
    expect(minutesLine(16)).toBe('16 min read');
  });
});

describe("the chips' labels", () => {
  it("are read by key from a list's own answer, and are none before there is one", () => {
    const facet = (key: string, label: string) => ({ key, label, count: 1 });
    const list = {
      cards: [],
      total: 0,
      topics: [facet('ai', 'AI')],
      keywords: [facet('cqrs', 'CQRS')],
      months: [facet('2026-11', 'November 2026')],
      next: null,
      terms: [],
    };
    expect(labelsOf(list)).toEqual({
      topics: { ai: 'AI' },
      keywords: { cqrs: 'CQRS' },
      months: { '2026-11': 'November 2026' },
    });
    expect(labelsOf(null)).toBeNull();
  });
});
