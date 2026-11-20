import { describe, expect, it } from 'vitest';
import { DOCS, type DocKey } from './documents';

describe('the catalogue (the quality1120 picture)', () => {
  it('opens only the Quality page in the wide window, with its note on when its numbers were read', () => {
    const wide = (Object.keys(DOCS) as DocKey[]).filter((key) => DOCS[key].wide);
    expect(wide).toEqual(['quality']);
    expect(DOCS.quality.note).toBe('Live · read when opened');
    const noted = (Object.keys(DOCS) as DocKey[]).filter((key) => DOCS[key].note !== undefined);
    expect(noted).toEqual(['quality']);
    expect(DOCS.quality.title).toBe('Quality you can watch');
  });
});
