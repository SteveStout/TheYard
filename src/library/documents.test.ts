import { describe, expect, it } from 'vitest';
import { DOCS, type DocKey } from './documents';

describe('the catalogue (the quality1120 picture)', () => {
  it('opens only the Quality page in the wide window, its bar with no note now its dark live panel says LIVE', () => {
    const wide = (Object.keys(DOCS) as DocKey[]).filter((key) => DOCS[key].wide);
    expect(wide).toEqual(['quality']);
    // saleshome1120: "Live · read when opened" left the sheet's bar; the panel says it.
    const noted = (Object.keys(DOCS) as DocKey[]).filter((key) => DOCS[key].note !== undefined);
    expect(noted).toEqual([]);
    expect(DOCS.quality.title).toBe('Quality you can watch');
  });
});
