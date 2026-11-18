/**
 * Does:      The header badge's popover (ADR-130): what the running version had to
 *            pass, one line for each chip, the commit, and the way to every number on the Quality page. Loaded only
 *            when the badge is first opened, so the words and their styles stay out of every page's first load. Drawn
 *            into document.body through a portal, so the store bar's frost, which makes a stacking context and holds a
 *            fixed child, cannot trap it: its layer is the page's, and its top is the window's.
 * Does not:  Read anything: StoreBar hands in the badge's answer (gateFacts.ts), which the chips draw too. Nor raise a
 *            layer: the glass panels after the header stay as they are, and this draws over them by its own layer.
 * Used by:   StoreBar.tsx, by a lazy import.
 */
import { useEffect, useRef } from 'react';
import { createPortal } from 'react-dom';
import { qualityLine, type GateFacts } from '../StoreBar/gateFacts';
import styles from './GatePopover.module.css';

export function GatePopover({
  facts,
  top,
  onClose,
}: {
  facts: GateFacts;
  /** The badge's bottom edge, in the window, so the panel opens just under it. */
  top: number;
  onClose: () => void;
}) {
  const panel = useRef<HTMLDivElement>(null);

  // Escape or a click outside closes it, and it takes the focus so a keyboard reader lands inside.
  useEffect(() => {
    panel.current?.focus();
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    const onDown = (event: PointerEvent) => {
      const target = event.target as Node | null;
      if (
        target &&
        !panel.current?.contains(target) &&
        !(target as Element).closest?.('[data-testid="gate-badge"]')
      )
        onClose();
    };
    document.addEventListener('keydown', onKey);
    document.addEventListener('pointerdown', onDown);
    return () => {
      document.removeEventListener('keydown', onKey);
      document.removeEventListener('pointerdown', onDown);
    };
  }, [onClose]);

  const { quality, review } = facts;
  // Into the body: inside the store bar, its backdrop-filter held both the layer and the fixed top.
  return createPortal(
    <div
      ref={panel}
      className={`${styles.panel} op-glass`}
      style={{ top }}
      role="dialog"
      aria-labelledby="gate-popover-title"
      tabIndex={-1}
      data-testid="gate-popover"
    >
      <p className={styles.title} id="gate-popover-title">
        What v{facts.version} had to pass
      </p>
      <ul className={styles.lines}>
        <li>
          {facts.gated
            ? 'Every gate job green before it could ship: the build, both stores, the browser tests and the checks'
            : 'The gate’s reading of this version is not on file yet'}
        </li>
        <li>{quality ? qualityLine(quality) : 'Its quality numbers are not on file yet'}</li>
        <li>
          {review?.reviewed === 'full'
            ? `CodeRabbit read the pull request in full and found ${
                review.critical === 0 ? 'no Critical finding' : `${review.critical} Critical`
              }; a Critical finding holds the promote`
            : 'CodeRabbit’s read of this version is not on file'}
        </li>
      </ul>
      {facts.commit && <p className={styles.commit}>Commit {facts.commit}</p>}
      <a className={styles.link} href="/?doc=quality">
        See every number on the Quality page
      </a>
    </div>,
    document.body
  );
}
