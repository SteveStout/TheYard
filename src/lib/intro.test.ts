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
    expect(INTRO.sentence).toContain('Steven Stout');
    expect(INTRO.sentence).toMatch(/auction site/);
    expect(INTRO.sentence.split('. ').length).toBe(1);
    expect(INTRO.links.map((link) => link.key)).toEqual(['resume', 'author', 'built', 'admin']);
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
