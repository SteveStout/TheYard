import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FADE_MS, LOCK_AFTER_MS, counted, createWorking, working } from './working';

describe('the working signal (ADR: One working lock for the whole site, after half a second)', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('never locks for a request that ends before half a second', () => {
    const store = createWorking();
    const heard = vi.fn();
    store.subscribe(heard);
    const token = store.begin();
    vi.advanceTimersByTime(LOCK_AFTER_MS - 1);
    store.end(token);
    vi.advanceTimersByTime(1_000);
    expect(store.locked()).toBe(false);
    expect(heard).not.toHaveBeenCalled();
  });

  it('locks from 500 ms to 900 ms for a request that ends at 900', () => {
    const store = createWorking();
    const token = store.begin();
    vi.advanceTimersByTime(499);
    expect(store.locked()).toBe(false);
    vi.advanceTimersByTime(1);
    expect(store.locked()).toBe(true);
    vi.advanceTimersByTime(400);
    expect(store.locked()).toBe(true);
    store.end(token);
    expect(store.locked()).toBe(false);
  });

  it('unlocks two overlapping requests on the last one, and says so once each way', () => {
    const store = createWorking();
    const heard = vi.fn();
    store.subscribe(heard);
    const first = store.begin();
    vi.advanceTimersByTime(100);
    const second = store.begin();
    vi.advanceTimersByTime(600);
    expect(store.locked()).toBe(true);
    store.end(first);
    expect(store.locked()).toBe(true);
    store.end(second);
    expect(store.locked()).toBe(false);
    expect(heard).toHaveBeenCalledTimes(2);
  });

  it('never locks for a background request, however long it runs', () => {
    const store = createWorking();
    const token = store.begin({ background: true });
    vi.advanceTimersByTime(10_000);
    expect(store.locked()).toBe(false);
    store.end(token);
    expect(store.locked()).toBe(false);
  });

  it('unlocks when a counted request fails, and the failure still reaches the caller', async () => {
    const failing = counted(
      () =>
        new Promise<never>((_, reject) => {
          setTimeout(() => reject(new Error('no answer')), 900);
        })
    );
    vi.advanceTimersByTime(LOCK_AFTER_MS);
    expect(working.locked()).toBe(true);
    vi.advanceTimersByTime(400);
    await expect(failing).rejects.toThrow('no answer');
    expect(working.locked()).toBe(false);
  });

  it('waits half a second and fades in less than a fifth of one', () => {
    expect(FADE_MS).toBe(180);
    expect(LOCK_AFTER_MS).toBe(500);
  });
});
