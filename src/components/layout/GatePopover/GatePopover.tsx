/**
 * Does:      The header badge's popover (ADR-130): what the running version had to
 *            pass, one line for each chip, the commit, the gate run that shipped it, and the way to every number on
 *            the Quality page. Loaded only
 *            when the badge is first opened, so the words and their styles stay out of every page's first load.
 * Does not:  Read anything: StoreBar hands in the badge's answer (gateFacts.ts), which the chips draw too.
 * Used by:   StoreBar.tsx, by a lazy import.
 */
import { useEffect, useRef } from 'react';
import { gateRunUrl, qualityLine, type GateFacts } from '../StoreBar/gateFacts';
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
  return (
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
      {facts.run !== null && (
        <a
          className={styles.link}
          href={gateRunUrl(facts.run)}
          target="_blank"
          rel="noopener noreferrer"
          data-testid="gate-popover-run"
        >
          Open the gate run that shipped it
        </a>
      )}
      <a className={styles.link} href="/?doc=quality">
        See every number on the Quality page
      </a>
    </div>
  );
}
