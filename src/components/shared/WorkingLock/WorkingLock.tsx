import { type CSSProperties, type Ref, type SyntheticEvent, useEffect, useId, useRef } from 'react';
import { useWorking } from '../../../hooks/useWorking';
import { FADE_MS } from '../../../lib/working';
import styles from './WorkingLock.module.css';

/**
 * The one working lock (ADR: One working lock for the whole site, after half a
 * second): once a request a person started has run half a second, a frosted
 * glass lock covers the whole page, the page showing through it, and takes
 * every click and key until the last such request settles and its short hold has passed; then it
 * fades. One panel, the shared glass with its rule and corner brackets, holds a graduated ring with a
 * gold sweep and a thin teal arc turning the other way, and the fixed words. It says nothing about
 * what is being done. Mounted once, in the shell; nothing else draws it. It stays in the page, out of
 * the layout while nothing is slow, so the fade out is the sheet's transition and no state follows the
 * store. A hidden status line, always in the page, says "Working" while it is up, so a screen reader
 * hears it once; the turning dots are hidden from it.
 */
export function WorkingLock() {
  const locked = useWorking();
  const panel = useRef<HTMLDivElement>(null);
  const before = useRef<Element | null>(null);

  // Focus waits on the lock while it is up and goes back where it was after, or to the main
  // landmark when the answer replaced the element that had it.
  useEffect(() => {
    if (!locked) return;
    before.current = document.activeElement;
    panel.current?.focus();
    window.addEventListener('keydown', swallow, true);
    return () => {
      window.removeEventListener('keydown', swallow, true);
      returnFocus(before.current, document);
    };
  }, [locked]);

  return <WorkingLayers locked={locked} panel={panel} />;
}

/** Stops an event from reaching the page under the lock. */
export function swallow(event: Pick<SyntheticEvent | Event, 'preventDefault' | 'stopPropagation'>) {
  event.preventDefault();
  event.stopPropagation();
}

/** Gives focus back to the element that had it, or to the main landmark when it is gone. */
export function returnFocus(
  before: Element | null,
  page: Pick<Document, 'contains' | 'querySelector'>
): void {
  const element = before as HTMLElement | null;
  if (element !== null && typeof element.focus === 'function' && page.contains(element)) {
    element.focus();
    return;
  }
  page.querySelector<HTMLElement>('main')?.focus();
}

/** The status line and the lock as drawn for one value of locked: what the store decides, drawn. */
export function WorkingLayers({ locked, panel }: { locked: boolean; panel?: Ref<HTMLDivElement> }) {
  const fade = { '--working-fade': `${FADE_MS}ms` } as CSSProperties;
  return (
    <>
      <span className={styles.said} role="status" aria-live="polite" data-testid="working-said">
        {locked ? 'Working' : ''}
      </span>
      <div
        className={`${styles.lock} ${locked ? styles.on : styles.off}`}
        style={fade}
        data-locked={locked}
        role="dialog"
        aria-modal="true"
        aria-label="Working"
        data-testid="working-lock"
        ref={panel}
        tabIndex={-1}
        onPointerDownCapture={(event) => event.preventDefault()}
        onClickCapture={swallow}
      >
        <WorkingPanel />
      </div>
    </>
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
          <b>
            Working
            <span className={styles.dots} aria-hidden="true" />
          </b>
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
