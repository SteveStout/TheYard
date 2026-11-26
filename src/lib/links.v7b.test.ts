import { describe, expect, it } from 'vitest';
import { holdCode, pageOpenReport, reportPath, withCode } from './links';

/** A session store that keeps what it is given, as sessionStorage does. */
const store = () => {
  const kept = new Map<string, string>();
  return {
    getItem: (key: string) => kept.get(key) ?? null,
    setItem: (key: string, value: string) => void kept.set(key, value),
  };
};

// Ladder v7b: the code a visit arrived with is held for the visit and carried on the footer's links, so each click
// lands as an open under the page it opens; the blog and the Quality page report under their own names.
describe('a tagged visit, carried', () => {
  it('holds the code the visit arrived with, and only a code', () => {
    const session = store();
    expect(holdCode('?r=k7f2qa', session)).toBe('k7f2qa');
    expect(holdCode('', session)).toBe('k7f2qa');
    expect(holdCode('?r=Steve', store())).toBeNull();
  });

  it('puts the code on a link of this site and leaves another site’s alone', () => {
    expect(withCode('/about', 'k7f2qa')).toBe('/about?r=k7f2qa');
    expect(withCode('/?view=blog&post=a', 'k7f2qa')).toBe('/?view=blog&post=a&r=k7f2qa');
    expect(withCode('https://github.com/SteveStout/TheYard', 'k7f2qa')).toBe(
      'https://github.com/SteveStout/TheYard'
    );
    expect(withCode('/about', null)).toBe('/about');
  });

  it('puts the code before a section’s name, so the section is still found (the Quality foot’s first link)', () => {
    expect(withCode('/?view=review#questions', 'k7f2qa')).toBe('/?view=review&r=k7f2qa#questions');
    expect(withCode('/#top', 'k7f2qa')).toBe('/?r=k7f2qa#top');
    expect(withCode('/?view=review#questions', null)).toBe('/?view=review#questions');
  });

  it('reports the blog and the Quality page under their own names', () => {
    expect(reportPath('?view=blog&post=a', '/')).toBe('/blog');
    expect(reportPath('?doc=quality', '/')).toBe('/quality');
    expect(reportPath('', '/review')).toBe('/review');
  });

  it('reports a sent About link under /about, the path the Links card and the follow-up rung read', () => {
    expect(reportPath('?doc=author&r=k7f2qa', '/')).toBe('/about');
    expect(reportPath('?view=about&r=k7f2qa', '/')).toBe('/about');
    expect(pageOpenReport('?doc=author&r=k7f2qa', reportPath('?doc=author&r=k7f2qa', '/'))).toBe(
      '/api/links/open?r=k7f2qa&path=/about'
    );
  });
});
