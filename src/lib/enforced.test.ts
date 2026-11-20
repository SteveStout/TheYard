import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchEnforced, heldBy, heldByText, heldCounts, recordMark } from './enforced';

/**
 * The Decision Records index's marks (ADR-029, the addendum on the marks). What
 * is worth holding here: a record a rule cites reads as held by a test and one
 * no rule cites as held by review, a record the API does not know carries no
 * mark rather than a wrong one, and an API that does not answer leaves the
 * index without marks instead of breaking it.
 */

function reply(status: number, body: unknown): Response {
  return { ok: status >= 200 && status < 300, status, json: async () => body } as Response;
}

describe('the Decision Records marks', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('reads a cited record as held by a test and an uncited one as held by review', () => {
    const enforced = { '029': 0, '075': 12 };
    expect(heldBy(enforced, '075')).toBe('test');
    expect(heldBy(enforced, '029')).toBe('review');
  });

  it('marks nothing before the marks are read or for a record the API does not know', () => {
    expect(heldBy(null, '075')).toBeNull();
    expect(heldBy({ '075': 12 }, '136')).toBeNull();
    expect(heldBy({ '075': 12 }, undefined)).toBeNull();
  });

  it('says how many rules hold a record, in words a screen reader reads', () => {
    const enforced = { '029': 0, '031': 1, '075': 12 };
    expect(heldByText(enforced, '029')).toBe('Held by review');
    expect(heldByText(enforced, '031')).toBe('Held by a test: one rule cites it');
    expect(heldByText(enforced, '075')).toBe('Held by tests: 12 rules cite it');
  });

  it('gives a row its mark with its words, and counts the records each holder holds', () => {
    const enforced = { '029': 0, '030': 0, '075': 12 };
    expect(recordMark(enforced, '075')).toEqual({
      by: 'test',
      text: 'Held by tests: 12 rules cite it',
    });
    expect(recordMark(enforced, '029')).toEqual({ by: 'review', text: 'Held by review' });
    expect(recordMark(null, '075')).toBeNull();
    expect(heldCounts(enforced)).toEqual({ test: 1, review: 2 });
  });

  it('reads the marks from the API, and an API that does not answer gives none', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply(200, { '075': 12 }));
    vi.stubGlobal('fetch', fetch);
    expect(await fetchEnforced()).toEqual({ '075': 12 });
    expect(fetch).toHaveBeenCalledWith('/api/docs/enforced', expect.anything());

    fetch.mockResolvedValueOnce(reply(503, {}));
    expect(await fetchEnforced()).toBeNull();

    fetch.mockRejectedValueOnce(new TypeError('offline'));
    expect(await fetchEnforced()).toBeNull();
  });
});
