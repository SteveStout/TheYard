import { useEffect, useId, useRef } from 'react';
import { useWorking } from '../../../hooks/useWorking';
import styles from './WorkingLock.module.css';

/**
 * The one working lock (ADR: One working lock for the whole site, after half a
 * second): once a request a person started has run half a second, a frosted
 * glass lock covers the whole page, the page showing through it, and takes
 * every click and key until the last such request settles; then it fades. One
 * panel, the shared glass with its rule and corner brackets, holds a graduated ring with a gold sweep and a
 * thin teal arc turning the other way, and the fixed words. It says nothing
 * about what is being done. Mounted once, in the shell; nothing else draws it.
 * It stays in the page, out of the layout while nothing is slow, so the fade
 * out is the sheet's transition and no state follows the store.
 */
export function WorkingLock() {
  const locked = useWorking();
  const panel = useRef<HTMLDivElement>(null);
  const before = useRef<Element | null>(null);

  // Focus waits on the lock while it is up and goes back where it was after.
  useEffect(() => {
    if (!locked) return;
    before.current = document.activeElement;
    panel.current?.focus();
    const swallow = (event: KeyboardEvent) => {
      event.preventDefault();
      event.stopPropagation();
    };
    window.addEventListener('keydown', swallow, true);
    return () => {
      window.removeEventListener('keydown', swallow, true);
      if (before.current instanceof HTMLElement) before.current.focus();
    };
  }, [locked]);

  return (
    <div
      className={`${styles.lock} ${locked ? styles.on : styles.off}`}
      data-locked={locked}
      role="status"
      aria-live="polite"
      aria-label="Working"
      data-testid="working-lock"
      ref={panel}
      tabIndex={-1}
      onPointerDownCapture={(event) => event.preventDefault()}
      onClickCapture={(event) => {
        event.preventDefault();
        event.stopPropagation();
      }}
    >
      <WorkingPanel />
    </div>
  );
}

/** The panel the lock holds: the shared glass (.op-glass, which draws the rule and the brackets), the ring and the fixed words. Its gradient's id is its own, so a sample beside the lock never borrows it. */
function WorkingPanel() {
  // An id an SVG reference can carry: React's own holds characters a url() reference does not need.
  const gold = 'working-gold-' + useId().replace(/[^a-zA-Z0-9]/g, '');
  return (
    <div className={`${styles.panel} op-glass`}>
      <div className={styles.hud}>
        <svg viewBox="0 0 240 240" aria-hidden="true" focusable="false">
          <defs>
            <linearGradient id={gold} x1="0" x2="1">
              <stop offset="0" className={styles.goldDark} />
              <stop offset="0.5" className={styles.goldLight} />
              <stop offset="1" className={styles.goldDark} />
            </linearGradient>
          </defs>
          <circle className={styles.glass} cx="120" cy="120" r="78" />
          <circle className={styles.track} cx="120" cy="120" r="96" />
          <circle className={styles.ticks} cx="120" cy="120" r="108" />
          <circle className={styles.sweep} cx="120" cy="120" r="96" stroke={`url(#${gold})`} />
          <circle className={styles.inner} cx="120" cy="120" r="66" />
        </svg>
        <div className={styles.words}>
          <b>Working</b>
          <span>the yard is answering</span>
        </div>
      </div>
    </div>
  );
}

/**
 * The lock's look, still and in place, for the Style section: the same panel the
 * lock draws, without the cover, the focus or the keys.
 */
export function WorkingLockLook() {
  return (
    <div className={styles.sample} data-testid="working-lock-look">
      <WorkingPanel />
    </div>
  );
}
