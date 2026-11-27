import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SEARCH_WAIT_MS, listReader } from './blogReads';
import type { BlogList } from './blog';

const list = (total: number): BlogList => ({
  cards: [],
  total,
  topics: [],
  keywords: [],
  months: [],
  next: null,
  terms: [],
});

describe('the list reader', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('waits 300 ms after the last key and asks the API once, for the last thing typed', async () => {
    const read = vi.fn(async (path: string) => list(path.length));
    const answers: (BlogList | null)[] = [];
    const reader = listReader(read, (_, answer) => answers.push(answer));
    reader.ask('/api/blog/posts?q=c', 'wait');
    reader.ask('/api/blog/posts?q=co', 'wait');
    reader.ask('/api/blog/posts?q=cop', 'wait');
    await vi.advanceTimersByTimeAsync(SEARCH_WAIT_MS - 1);
    expect(read).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1);
    expect(read).toHaveBeenCalledTimes(1);
    expect(read.mock.calls[0][0]).toBe('/api/blog/posts?q=cop');
    expect(answers).toEqual([list('/api/blog/posts?q=cop'.length)]);
    expect(SEARCH_WAIT_MS).toBe(300);
  });

  it('waits from the last key when a later key holds back the address it asks for', async () => {
    const read = vi.fn(async (path: string) => list(path.length));
    const reader = listReader(read, () => {});
    reader.typed('/api/blog/posts?q=c');
    reader.ask('/api/blog/posts?q=c', 'wait');
    // "o" to "t": six keys 40 ms apart while the page draws, and no address arrives until it commits.
    for (const typed of ['co', 'cop', 'copi', 'copil', 'copilo', 'copilot']) {
      await vi.advanceTimersByTimeAsync(40);
      reader.typed(`/api/blog/posts?q=${typed}`);
    }
    await vi.advanceTimersByTimeAsync(SEARCH_WAIT_MS - 1);
    expect(read).not.toHaveBeenCalled();
    reader.ask('/api/blog/posts?q=copilot', 'wait');
    await vi.advanceTimersByTimeAsync(SEARCH_WAIT_MS);
    expect(read.mock.calls.map((call) => call[0])).toEqual(['/api/blog/posts?q=copilot']);
    reader.typed('/api/blog/posts?q=copilot');
    await vi.advanceTimersByTimeAsync(SEARCH_WAIT_MS);
    expect(read).toHaveBeenCalledTimes(1);
  });

  it('never asks for a word the box has typed past while the newer address is still being drawn', async () => {
    const read = vi.fn(async (path: string) => list(path.length));
    const reader = listReader(read, () => {});
    reader.typed('/api/blog/posts?q=copilo');
    reader.ask('/api/blog/posts?q=copilo', 'wait');
    // "t" is pressed and its page takes longer than the whole wait to commit, as on a loaded runner.
    await vi.advanceTimersByTimeAsync(40);
    reader.typed('/api/blog/posts?q=copilot');
    await vi.advanceTimersByTimeAsync(SEARCH_WAIT_MS * 2);
    expect(read).not.toHaveBeenCalled();
    reader.ask('/api/blog/posts?q=copilot', 'wait');
    await vi.advanceTimersByTimeAsync(SEARCH_WAIT_MS);
    expect(read.mock.calls.map((call) => call[0])).toEqual(['/api/blog/posts?q=copilot']);
  });

  it('still asks when a key leaves the address as it was, and a key whose address never arrives holds nothing after it', async () => {
    const read = vi.fn(async (path: string) => list(path.length));
    const reader = listReader(read, () => {});
    reader.typed('/api/blog/posts?q=copilot');
    reader.ask('/api/blog/posts?q=copilot', 'wait');
    // A trailing space: the key reads back to the same address, so no new one arrives.
    reader.typed('/api/blog/posts?q=copilot');
    await vi.advanceTimersByTimeAsync(SEARCH_WAIT_MS);
    // "x" then Backspace: "copilotx" arrives, "copilot" is on screen already and never asks again.
    reader.typed('/api/blog/posts?q=copilotx');
    reader.ask('/api/blog/posts?q=copilotx', 'wait');
    reader.typed('/api/blog/posts?q=copilot');
    await vi.advanceTimersByTimeAsync(SEARCH_WAIT_MS);
    // Back walks to the address before, as a wait, and it is read.
    reader.ask('/api/blog/posts?q=llm', 'wait');
    await vi.advanceTimersByTimeAsync(SEARCH_WAIT_MS);
    expect(read.mock.calls.map((call) => call[0])).toEqual([
      '/api/blog/posts?q=copilot',
      '/api/blog/posts?q=llm',
    ]);
  });

  it('aborts the read a newer ask replaces, and never hands on its answer', async () => {
    const signals: AbortSignal[] = [];
    let finish: (value: BlogList) => void = () => {};
    const read = vi.fn((path: string, signal: AbortSignal) => {
      signals.push(signal);
      return path.includes('slow')
        ? new Promise<BlogList>((resolve) => (finish = resolve))
        : Promise.resolve(list(2));
    });
    const seen: string[] = [];
    const reader = listReader(read, (path) => seen.push(path));
    reader.ask('/api/blog/posts?q=slow', 'now');
    reader.ask('/api/blog/posts?topic=ai', 'now');
    expect(signals[0].aborted).toBe(true);
    expect(signals[1].aborted).toBe(false);
    finish(list(1));
    await vi.runAllTimersAsync();
    expect(seen).toEqual(['/api/blog/posts?topic=ai']);
  });

  it('aborts a next page when a newer ask replaces the list, and never hands that page on', async () => {
    const signals: AbortSignal[] = [];
    let finish: (value: BlogList) => void = () => {};
    const read = vi.fn((path: string, signal: AbortSignal) => {
      signals.push(signal);
      return path.includes('after=')
        ? new Promise<BlogList>((resolve) => (finish = resolve))
        : Promise.resolve(list(2));
    });
    const pages: (BlogList | null)[] = [];
    const reader = listReader(read, () => {});
    reader.more('/api/blog/posts?after=abc', (page) => pages.push(page));
    reader.ask('/api/blog/posts?topic=ai', 'now');
    expect(signals[0].aborted).toBe(true);
    finish(list(9));
    await vi.runAllTimersAsync();
    expect(pages).toEqual([]);
  });

  it('hands on a next page nothing replaced', async () => {
    const read = vi.fn(async (_path: string) => list(7));
    const pages: (BlogList | null)[] = [];
    const reader = listReader(read, () => {});
    reader.more('/api/blog/posts?after=abc', (page) => pages.push(page));
    await vi.runAllTimersAsync();
    expect(pages).toEqual([list(7)]);
  });

  it('hands on a failed next page as null', async () => {
    const pages: (BlogList | null)[] = [];
    const reader = listReader(
      async () => Promise.reject(new Error('the network')),
      () => {}
    );
    reader.more('/api/blog/posts?after=abc', (page) => pages.push(page));
    await vi.runAllTimersAsync();
    expect(pages).toEqual([null]);
  });

  it('never hands on a next page that fails after stop aborted it', async () => {
    let fail: (reason: Error) => void = () => {};
    const pages: (BlogList | null)[] = [];
    const reader = listReader(
      () => new Promise<BlogList>((_, reject) => (fail = reject)),
      () => {}
    );
    reader.more('/api/blog/posts?after=abc', (page) => pages.push(page));
    reader.stop();
    fail(new Error('aborted'));
    await vi.runAllTimersAsync();
    expect(pages).toEqual([]);
  });

  it('never reports a replaced read that then fails as a failure', async () => {
    let fail: (reason: Error) => void = () => {};
    const read = vi.fn((path: string) =>
      path.includes('slow')
        ? new Promise<BlogList>((_, reject) => (fail = reject))
        : Promise.resolve(list(2))
    );
    const failures: boolean[] = [];
    const reader = listReader(read, (_path, _answer, failed) => failures.push(failed));
    reader.ask('/api/blog/posts?q=slow', 'now');
    reader.ask('/api/blog/posts?topic=ai', 'now');
    fail(new Error('aborted'));
    await vi.runAllTimersAsync();
    expect(failures).toEqual([false]);
  });

  it('asks at once for a chip, and a pending search gives way to it', async () => {
    const read = vi.fn(async (_path: string) => list(1));
    const reader = listReader(read, () => {});
    reader.ask('/api/blog/posts?q=on', 'wait');
    reader.ask('/api/blog/posts?topic=architecture', 'now');
    await vi.runAllTimersAsync();
    expect(read.mock.calls.map((call) => call[0])).toEqual(['/api/blog/posts?topic=architecture']);
  });

  it('says a failed read as a failure and stops cleanly', async () => {
    const answers: unknown[] = [];
    const reader = listReader(
      async () => Promise.reject(new Error('the network')),
      (_, answer, failed) => answers.push([answer, failed])
    );
    reader.ask('/api/blog/posts', 'now');
    await vi.runAllTimersAsync();
    expect(answers).toEqual([[null, true]]);
    const quiet = vi.fn(async () => list(1));
    const stopped = listReader(quiet, () => {});
    stopped.ask('/api/blog/posts?q=x', 'wait');
    stopped.stop();
    await vi.runAllTimersAsync();
    expect(quiet).not.toHaveBeenCalled();
  });
});
