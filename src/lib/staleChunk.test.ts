import { describe, expect, it, vi } from 'vitest';
import { isStaleChunk, RELOAD_WINDOW_MS, RELOADED_AT, reloadOnce } from './staleChunk';

const memory = () => {
  const values = new Map<string, string>();
  return {
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => void values.set(key, value),
    values,
  };
};

describe('isStaleChunk', () => {
  it("knows each engine's words for a chunk that is not on the server any more", () => {
    expect(isStaleChunk(new TypeError('Importing a module script failed.'))).toBe(true);
    expect(
      isStaleChunk(
        new TypeError(
          'Failed to fetch dynamically imported module: https://x/assets/ErrorsCard-abc.js'
        )
      )
    ).toBe(true);
    expect(isStaleChunk(new TypeError('error loading dynamically imported module'))).toBe(true);
    expect(isStaleChunk('Unable to preload CSS for /assets/AdminPanel-abc.css')).toBe(true);
  });

  it('leaves every other error to the boundary', () => {
    expect(isStaleChunk(new Error('Cannot read properties of undefined'))).toBe(false);
    expect(isStaleChunk(null)).toBe(false);
  });
});

describe('reloadOnce', () => {
  it('reloads, and remembers when', () => {
    const storage = memory();
    const reload = vi.fn();
    expect(reloadOnce(storage, reload, { now: 1_000_000 })).toBe(true);
    expect(reload).toHaveBeenCalledTimes(1);
    expect(storage.values.get(RELOADED_AT)).toBe('1000000');
  });

  it('does not reload a second time inside the window, so a real failure is shown, not looped', () => {
    const storage = memory();
    const reload = vi.fn();
    reloadOnce(storage, reload, { now: 1_000_000 });
    expect(reloadOnce(storage, reload, { now: 1_000_000 + RELOAD_WINDOW_MS - 1 })).toBe(false);
    expect(reload).toHaveBeenCalledTimes(1);
    expect(reloadOnce(storage, reload, { now: 1_000_000 + RELOAD_WINDOW_MS + 1 })).toBe(true);
    expect(reload).toHaveBeenCalledTimes(2);
  });

  it('does not reload a page that is offline: the same words come from a chunk that had no network', () => {
    const storage = memory();
    const reload = vi.fn();
    expect(reloadOnce(storage, reload, { now: 1_000_000, online: false })).toBe(false);
    expect(reload).not.toHaveBeenCalled();
    expect(storage.values.has(RELOADED_AT)).toBe(false);
  });

  it('shows the error where there is no storage to remember the reload in', () => {
    const reload = vi.fn();
    expect(reloadOnce(null, reload)).toBe(false);
    const refusing = {
      getItem: () => {
        throw new Error('denied');
      },
      setItem: () => undefined,
    };
    expect(reloadOnce(refusing, reload)).toBe(false);
    expect(reload).not.toHaveBeenCalled();
  });
});
