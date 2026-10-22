import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  FADE_MS,
  GIVE_UP_MS,
  HOLD_MS,
  LOCK_AFTER_MS,
  counted,
  createWorking,
  giveUp,
  working,
} from './working';

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
    // Shown at 500, so held to 850 though the last answer came at 700.
    expect(store.locked()).toBe(true);
    vi.advanceTimersByTime(150);
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

  it('holds a lock that has shown: one that would clear at 510 ms clears at 850', () => {
    const store = createWorking();
    const heard = vi.fn();
    store.subscribe(heard);
    const token = store.begin();
    vi.advanceTimersByTime(510);
    store.end(token);
    expect(store.locked()).toBe(true);
    vi.advanceTimersByTime(339);
    expect(store.locked()).toBe(true);
    vi.advanceTimersByTime(1);
    expect(store.locked()).toBe(false);
    expect(heard).toHaveBeenCalledTimes(2);
  });

  it('gives up on a request that hangs: it rejects at twenty seconds and the lock lets go', async () => {
    const signal = giveUp();
    const hung = counted(
      () =>
        new Promise<never>((_, reject) => {
          signal.addEventListener('abort', () => reject(signal.reason));
        })
    );
    const outcome = hung.catch((error: unknown) => error);
    vi.advanceTimersByTime(LOCK_AFTER_MS);
    expect(working.locked()).toBe(true);
    vi.advanceTimersByTime(GIVE_UP_MS - LOCK_AFTER_MS);
    expect(((await outcome) as DOMException).name).toBe('TimeoutError');
    expect(working.locked()).toBe(false);
  });

  it('waits half a second, holds a shown lock 350 ms, fades in 180 and gives up at twenty seconds', () => {
    expect(FADE_MS).toBe(180);
    expect(LOCK_AFTER_MS).toBe(500);
    expect(HOLD_MS).toBe(350);
    expect(GIVE_UP_MS).toBe(20_000);
  });
});
