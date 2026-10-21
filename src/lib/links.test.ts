import { describe, expect, it } from 'vitest';
import { linkCode, openStamp, pageOpenReport, pathsLine } from './links';

describe('tagged links', () => {
  it('reads a code off the address and nothing that is not one', () => {
    expect(linkCode('?r=k7f2qa')).toBe('k7f2qa');
    expect(linkCode('?view=about&r=k7f2qa')).toBe('k7f2qa');
    expect(linkCode('?r=Steve')).toBeNull();
    expect(linkCode('?r=k7f2qa7')).toBeNull();
    expect(linkCode('')).toBeNull();
  });

  it('reports the page open once, for a code, under the home view', () => {
    expect(pageOpenReport('?r=k7f2qa')).toBe('/api/links/open?r=k7f2qa&path=/');
    expect(pageOpenReport('?view=inventory')).toBeNull();
  });

  it('prints a row s paths busiest first and an open as a UTC minute', () => {
    expect(pathsLine({ '/resume/email': 1, '/': 2, '/api/go/github': 1 })).toBe(
      '/ 2 · /api/go/github 1 · /resume/email 1'
    );
    expect(openStamp(Date.UTC(2026, 9, 20, 17, 5))).toBe('2026-10-20 17:05 UTC');
  });
});
