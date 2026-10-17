import { describe, expect, it } from 'vitest';
import { INTRO, dismissIntro, introDismissed } from './intro';

const memory = () => {
  const held = new Map<string, string>();
  return {
    getItem: (key: string) => held.get(key) ?? null,
    setItem: (key: string, value: string) => void held.set(key, value),
  };
};

describe('the intro strip', () => {
  it('says what this is and who built it in one sentence, and offers four ways on', () => {
    expect(INTRO.sentence).toMatch(/one engineer/);
    expect(INTRO.sentence).toMatch(/auction/);
    expect(INTRO.sentence.split('. ').length).toBe(1);
    expect(INTRO.links.map((link) => link.key)).toEqual(['resume', 'author', 'built', 'admin']);
  });

  it('carries no figure of its own, because every figure the first screen shows is read live', () => {
    // A number typed into a sentence is a number nobody edits when it moves; the band over the
    // page reads its figures from the running site instead (ADR: One reading for the first screen).
    expect(INTRO.sentence).not.toMatch(/\d/);
  });

  it('stays dismissed in a browser that remembers', () => {
    const store = memory();
    expect(introDismissed(store)).toBe(false);
    dismissIntro(store);
    expect(introDismissed(store)).toBe(true);
  });

  it('comes back, and never throws, where the store is missing or refuses', () => {
    const refusing = {
      getItem: () => {
        throw new Error('blocked');
      },
      setItem: () => {
        throw new Error('blocked');
      },
    };
    expect(introDismissed(undefined)).toBe(false);
    expect(introDismissed(refusing)).toBe(false);
    expect(() => dismissIntro(refusing)).not.toThrow();
    expect(() => dismissIntro(undefined)).not.toThrow();
  });
});
