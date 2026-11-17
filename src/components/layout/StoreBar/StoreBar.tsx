import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react';
import {
  fetchStores,
  note,
  readyCount,
  segments,
  shortNote,
  type Stores,
} from '../../../lib/stores';
import { Ring } from '../../shared/Ring';
import styles from './StoreBar.module.css';
import { gateChips, gateFacts } from './gateFacts';

// The badge's popover is fetched the first time it opens, so its words and styles stay out of the first load
// (ADR-134, .github/byte-budgets.json).
const GatePopover = lazy(() =>
  import('../GatePopover/GatePopover').then((module) => ({ default: module.GatePopover }))
);

/** What this build's version had to pass: read once, from what the build carries. */
const GATE = gateFacts();
const CHIPS = gateChips(GATE);

/**
 * The bar at the top of the page (ADR: One container, both stores, and its
 * addendum on the toggle moving to the sites): which site this is, and one
 * click to the other. The other segment is a link to the other site at this
 * same path and query, so the address bar changes and the page that arrives
 * is that site's own. The band is drawn from the first paint at its full
 * height, so the page under it does not move when the answer arrives
 * (measured at 1280 without it: the whole page shifted 83 px, a layout shift
 * of 0.12, when this bar appeared); the segments are drawn only once the
 * server has answered, so a container that names no other site shows the
 * other segment as not here rather than as a control that does nothing.
 */
export function StoreBar() {
  const [stores, setStores] = useState<Stores | null>(null);
  const [gateTop, setGateTop] = useState<number | null>(null);
  const badge = useRef<HTMLButtonElement>(null);
  const closeGate = useCallback(() => setGateTop(null), []);

  useEffect(() => {
    const controller = new AbortController();
    void fetchStores(controller.signal).then((answer) => {
      if (answer) setStores(answer);
    });
    return () => controller.abort();
  }, []);

  // The build draws the landing page where there is no window: the bare address, which is what it is.
  const here =
    typeof window === 'undefined'
      ? { pathname: '/', search: '' }
      : { pathname: window.location.pathname, search: window.location.search };
  const ready = stores === null ? null : readyCount(stores);
  const readySentence = ready === null ? undefined : `${ready.up} of ${ready.of} stores ready`;

  return (
    <div
      className={styles.bar}
      data-testid="store-bar"
      data-state={stores === null ? 'loading' : 'ready'}
    >
      <div className={styles.inner}>
        <span className={styles.label} id="store-bar-label">
          Store
        </span>
        <nav
          className={`${styles.toggle} op-seg`}
          aria-labelledby="store-bar-label"
          aria-busy={stores === null}
        >
          {(stores === null ? [] : segments(stores, here)).map((segment) =>
            segment.current ? (
              <span
                key={segment.key}
                className={styles.segment}
                data-store={segment.key}
                aria-current="page"
                title={segment.title}
              >
                {segment.label}
              </span>
            ) : segment.href !== null ? (
              <a
                key={segment.key}
                className={styles.segment}
                data-store={segment.key}
                href={segment.href}
                title={segment.title}
              >
                {segment.label}
              </a>
            ) : (
              <span
                key={segment.key}
                className={styles.segment}
                data-store={segment.key}
                role="link"
                aria-disabled="true"
                title={segment.title}
              >
                {segment.label}
              </span>
            )
          )}
        </nav>
        {/* The stores that came up, of those this container runs (the
            operator's look): an empty track until the answer, at its size from
            the first paint, so the band does not move when it fills. */}
        <span className={styles.ready} title={readySentence}>
          {/* The words beside it say the count, so the ring is drawn and not read. */}
          <Ring
            value={ready === null ? 0 : ready.up}
            max={ready === null ? 1 : ready.of}
            size="tiny"
            label={null}
            testId="store-bar-ready"
          />
          {/* The count in words beside the ring, because a full tiny ring reads as a
              plain circle. Its room is held from the first paint. */}
          <span className={styles.readyWords} data-testid="store-bar-ready-words">
            {ready === null ? '' : `${ready.up}/${ready.of} ready`}
          </span>
        </span>
        <span className={styles.tail}>
          <span
            className={styles.note}
            data-testid="store-bar-note"
            title={stores === null ? undefined : note(stores)}
          >
            {/* The whole sentence from 1440, the short form under it (A6): no ellipsis at
              any width. One of the two is displayed at every width, so a screen reader
              always hears the note the eye reads. */}
            <span className={styles.noteLong}>{stores === null ? '' : note(stores)}</span>
            <span className={styles.noteShort}>{stores === null ? '' : shortNote(stores)}</span>
          </span>
          {/* The gate badge (ADR-130): what this version had to pass, read from
            what the build carries, so it is whole on the first paint. One chip at every width, because beside the
            note's whole sentence there is room for one and no more (store-toggle.spec), in the site's own pill
            group, which on a phone makes it a thumb-tall pill of its own in the note's row (hooks.spec, mobile.spec):
            the first load carries no new style for it. Its name and its popover say all three things. */}
          <span className="op-seg op-seg-wrap">
            <button
              ref={badge}
              type="button"
              className={styles.segment}
              style={{ cursor: 'pointer' }}
              aria-label={`${CHIPS.join(', ')}: what v${GATE.version} had to pass`}
              aria-haspopup="dialog"
              aria-expanded={gateTop !== null}
              data-testid="gate-badge"
              data-gated={GATE.gated}
              onClick={() =>
                setGateTop((open) =>
                  open !== null ? null : (badge.current?.getBoundingClientRect().bottom ?? 0) + 8
                )
              }
            >
              {CHIPS[0]}
            </button>
          </span>
        </span>
        {gateTop !== null && (
          <Suspense fallback={null}>
            <GatePopover facts={GATE} top={gateTop} onClose={closeGate} />
          </Suspense>
        )}
      </div>
    </div>
  );
}
