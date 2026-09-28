import { describe, expect, it } from 'vitest';
import { prefetchWhenIdle, shouldPrefetch, type Scheduler } from './prefetch';

const now: Scheduler = (work) => {
  work();
  return () => undefined;
};

function counting() {
  const calls: string[] = [];
  const loaders = ['markdown', 'admin'].map((name) => () => {
    calls.push(name);
    return Promise.resolve();
  });
  return { calls, loaders };
}

describe('the prefetch guard', () => {
  it('prefetches where the browser says nothing about the connection, or says it is fast', () => {
    expect(shouldPrefetch(undefined)).toBe(true);
    expect(shouldPrefetch({ effectiveType: '4g' })).toBe(true);
    expect(shouldPrefetch({ effectiveType: '3g', saveData: false })).toBe(true);
  });

  it('never prefetches when the reader asked to save data, or on a 2G connection', () => {
    expect(shouldPrefetch({ saveData: true, effectiveType: '4g' })).toBe(false);
    expect(shouldPrefetch({ effectiveType: '2g' })).toBe(false);
    expect(shouldPrefetch({ effectiveType: 'slow-2g' })).toBe(false);
  });

  it('fetches every chunk on a good connection, and none when the guard says no', () => {
    const fast = counting();
    prefetchWhenIdle(fast.loaders, { effectiveType: '4g' }, now);
    expect(fast.calls).toEqual(['markdown', 'admin']);

    const slowOnes = [{ saveData: true }, { effectiveType: '2g' }, { effectiveType: 'slow-2g' }];
    for (const slow of slowOnes) {
      const held = counting();
      let scheduled = false;
      prefetchWhenIdle(held.loaders, slow, (work) => {
        scheduled = true;
        work();
        return () => undefined;
      });
      expect(held.calls).toEqual([]);
      expect(scheduled).toBe(false);
    }
  });

  it('shrugs off a chunk that fails, so the click can fetch it again', async () => {
    const failing = () => Promise.reject(new Error('offline'));
    expect(() => prefetchWhenIdle([failing], undefined, now)).not.toThrow();
    await Promise.resolve();
  });
});
