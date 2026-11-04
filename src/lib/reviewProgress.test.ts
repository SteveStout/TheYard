import { describe, expect, it } from 'vitest';
import {
  CURRENT_KEY,
  emptyProgress,
  markReached,
  moveTo,
  newlyReached,
  reachBody,
  readProgress,
  REACHED_KEY,
  writeProgress,
  type StorageLike,
} from './reviewProgress';

/** A Storage held in a map, the shape localStorage has. */
function memory(seed: Record<string, string> = {}): StorageLike & { held: Map<string, string> } {
  const held = new Map(Object.entries(seed));
  return {
    held,
    getItem: (key) => held.get(key) ?? null,
    setItem: (key, value) => void held.set(key, value),
  };
}

/** A Storage that throws on every call, the way a private window's does. */
const refusing: StorageLike = {
  getItem: () => {
    throw new Error('SecurityError');
  },
  setItem: () => {
    throw new Error('QuotaExceededError');
  },
};

describe('the review place, kept on the browser (ADR: Ten minutes, seven stops)', () => {
  it('starts at the first stop with nothing reached', () => {
    expect(readProgress(memory())).toEqual({ current: 0, reached: [] });
    expect(emptyProgress()).toEqual({ current: 0, reached: [] });
  });

  it('reads back what it wrote, under its two keys', () => {
    const store = memory();
    writeProgress(store, { current: 3, reached: [0, 2, 1] });
    expect(store.held.get(CURRENT_KEY)).toBe('3');
    expect(store.held.get(REACHED_KEY)).toBe('[0,1,2]');
    expect(readProgress(store)).toEqual({ current: 3, reached: [0, 1, 2] });
  });

  it('reads anything it did not write as the first stop, never as a stop that is not there', () => {
    expect(readProgress(memory({ [CURRENT_KEY]: '9' })).current).toBe(0);
    expect(readProgress(memory({ [CURRENT_KEY]: 'two' })).current).toBe(0);
    expect(readProgress(memory({ [CURRENT_KEY]: '-1' })).current).toBe(0);
    expect(readProgress(memory({ [REACHED_KEY]: '{"a":1}' })).reached).toEqual([]);
    expect(readProgress(memory({ [REACHED_KEY]: '[1, 1, 12, "x", 4]' })).reached).toEqual([1, 4]);
    expect(readProgress(memory({ [REACHED_KEY]: 'not json' })).reached).toEqual([]);
  });

  it('keeps the page working when the browser refuses to keep anything', () => {
    expect(readProgress(refusing)).toEqual({ current: 0, reached: [] });
    expect(() => writeProgress(refusing, { current: 2, reached: [1] })).not.toThrow();
    expect(readProgress(null)).toEqual({ current: 0, reached: [] });
    expect(() => writeProgress(null, { current: 2, reached: [1] })).not.toThrow();
  });

  it('moves the place within the seven stops, and marks a stop reached once', () => {
    const start = emptyProgress();
    expect(moveTo(start, 1)).toEqual({ current: 1, reached: [] });
    expect(moveTo(start, 7).current).toBe(6);
    expect(moveTo(start, -1).current).toBe(0);
    const once = markReached(start, 2);
    expect(once.reached).toEqual([2]);
    expect(markReached(once, 2)).toBe(once);
    expect(markReached(once, 9)).toBe(once);
  });

  it('builds the report the page sends: the stop, and the code only when it is one', () => {
    expect(reachBody(0, 'zzrev1')).toEqual({ stop: 0, r: 'zzrev1' });
    expect(reachBody(3, null)).toEqual({ stop: 3 });
    expect(reachBody(3, 'Steve')).toEqual({ stop: 3 });
  });

  it('names the stops a move reached for the first time, each once, numbered as the route numbers them', () => {
    expect(newlyReached({ current: 0, reached: [] }, { current: 1, reached: [0] })).toEqual([1]);
    expect(newlyReached({ current: 1, reached: [0] }, { current: 2, reached: [0, 1] })).toEqual([
      2,
    ]);
    expect(newlyReached({ current: 2, reached: [0, 1] }, { current: 1, reached: [0, 1] })).toEqual(
      []
    );
  });
});
