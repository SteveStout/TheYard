/**
 * The site's one working signal (ADR: One working lock for the whole site, after
 * half a second): how many requests a person's action started are still in
 * flight and have run past half a second. The lock reads it; nothing else decides when
 * the screen locks. A request begins and ends here around its fetch, in
 * src/lib/data.ts, src/lib/auth.ts and src/lib/prefetch.ts, so no call site
 * changes. A background read (a refresh tick, the room's round, a prefetch, the
 * first load's own reads) says so and is never counted.
 *
 * No React in here: a small store with begin, end and subscribe, and a clock it
 * can be handed so a test drives the half second by hand.
 */

/**
 * How long a request may run before the screen locks: half a second, the owner's own figure. A
 * filter change on the Cosmos DB name answers in a little under that, so a shorter wait would flash
 * the lock on nearly every one of them, which is the one thing it must not do (the ADR has the
 * measurements).
 */
export const LOCK_AFTER_MS = 500;

/** How long the lock takes to fade once the last counted request settles; the lock's CSS reads it. */
export const FADE_MS = 180;

/**
 * Once the lock has shown it stays at least this long, so it is either absent or plainly there: a
 * lock that showed for 2 ms and faded over 180 was a ghost of the panel, not a lock.
 */
export const HOLD_MS = 350;

/** How long a person's request may hang before it is given up, its caller shows the error and the lock lets go. */
export const GIVE_UP_MS = 20_000;

/** A signal that aborts after GIVE_UP_MS on the page's own timer: hand it to a fetch a person started. */
export function giveUp(ms: number = GIVE_UP_MS): AbortSignal {
  const controller = new AbortController();
  setTimeout(
    () => controller.abort(new DOMException('The request took too long.', 'TimeoutError')),
    ms
  );
  return controller.signal;
}

/** What a request says about itself when it begins. */
export type WorkingOptions = {
  /** A read nobody clicked for: it never locks the screen. */
  background?: boolean;
};

/** The timer a store waits on, the browser's by default. */
export type WorkingClock = {
  set: (run: () => void, ms: number) => unknown;
  clear: (handle: unknown) => void;
};

type Pending = { handle: unknown; slow: boolean };

/** One store: begin returns a token, end takes it back, subscribe hears every change of locked. */
export function createWorking(clock: WorkingClock = browserClock) {
  const pending = new Map<number, Pending>();
  const listeners = new Set<() => void>();
  let next = 1;
  let slow = 0;
  let holding = false;

  const notify = () => {
    for (const listener of listeners) listener();
  };

  return {
    /** A request starts; it counts toward the lock once it has run LOCK_AFTER_MS, unless it is background. */
    begin(options: WorkingOptions = {}): number {
      const token = next++;
      if (options.background === true) return token;
      const entry: Pending = { handle: null, slow: false };
      entry.handle = clock.set(() => {
        entry.slow = true;
        slow++;
        if (slow !== 1 || holding) return;
        holding = true;
        clock.set(() => {
          holding = false;
          if (slow === 0) notify();
        }, HOLD_MS);
        notify();
      }, LOCK_AFTER_MS);
      pending.set(token, entry);
      return token;
    },
    /** A request settled, answered or failed; the lock clears when the last slow one does and the hold has passed. */
    end(token: number): void {
      const entry = pending.get(token);
      if (entry === undefined) return;
      pending.delete(token);
      clock.clear(entry.handle);
      if (entry.slow) {
        slow--;
        if (slow === 0 && !holding) notify();
      }
    },
    /** Whether a counted request has run past LOCK_AFTER_MS and not yet settled, or the lock is in its hold. */
    locked(): boolean {
      return slow > 0 || holding;
    },
    /** Hears every change of locked; returns the way to stop hearing it. */
    subscribe(listener: () => void): () => void {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
  };
}

const browserClock: WorkingClock = {
  set: (run, ms) => setTimeout(run, ms),
  clear: (handle) => clearTimeout(handle as ReturnType<typeof setTimeout>),
};

/** The site's one store. */
export const working = createWorking();

/** Runs a request between begin and end, so its time counts toward the lock however it settles. */
export async function counted<T>(run: () => Promise<T>, options: WorkingOptions = {}): Promise<T> {
  const token = working.begin(options);
  try {
    return await run();
  } finally {
    working.end(token);
  }
}
