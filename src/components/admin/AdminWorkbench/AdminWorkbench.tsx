// The Admin workbench's layout: the rail of cards, the open card large beside it, an optional
// pinned card, the "This hour" panel, and the j and k keys that walk the rail. It takes a
// function that draws a card by name, so it decides only where things go and never what a
// card holds. It is its own file because the layout and its screen-size rules are one job.
import { type ReactNode, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { BENCH_QUESTIONS, benchCard, neighbours, type HourGlance } from '../../../lib/bench';
import type { CardSlug } from '../../../lib/workbench';
import { useMediaQuery } from '../../../hooks/useMediaQuery';
import { PHONE, WIDE, WIDEST } from '../../../lib/breakpoints';
import styles from '../AdminPanel/AdminPanel.module.css';
import cardStyles from '../shared/card.module.css';
import { AdminRailContent } from '../AdminRailContent';
import { AdminHourAtAGlance } from '../AdminHourAtAGlance';

// #region workbench
/**
 * The workbench layout (ADR-080): the rail on the left, the open card large
 * beside it, and an optional second column for a pinned card.
 * The j and k keys move to the next and previous card, unless you are typing.
 * Under 1280px the rail moves into a drawer behind a Cards button. Under 640px
 * the pinned card becomes a fold under the open one and reads nothing until
 * you expand it.
 */
export function AdminWorkbench({
  open,
  pin,
  asked,
  onOpen,
  onPin,
  glance,
  render,
}: {
  /** The card open now. */
  open: CardSlug;
  /** The card pinned beside it, or null. */
  pin: CardSlug | null;
  /** A card name from the address that matched no card, or null. */
  asked: string | null;
  onOpen: (slug: CardSlug) => void;
  onPin: (slug: CardSlug | null) => void;
  /** The hour's summary for the "This hour" panel. */
  glance: HourGlance;
  /** Draws one card by its slug. */
  render: (slug: CardSlug) => ReactNode;
}) {
  const phone = useMediaQuery(PHONE);
  const wide = useMediaQuery(WIDEST);
  // Below the WIDE breakpoint the rail goes in the drawer. With both the site's
  // sidebar and this rail showing, a narrower screen leaves too little room for
  // the card, and its tables scroll sideways.
  const railInDrawer = !useMediaQuery(WIDE);
  const drawerRef = useRef<HTMLDialogElement>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [foldOpen, setFoldOpen] = useState(false);
  const { previous, next } = neighbours(open);
  const openCard = benchCard(open);
  const question = BENCH_QUESTIONS.find((entry) => entry.key === openCard.question);
  const pinShown = pin !== null && pin !== open;
  // "This hour" shows only with the cards in HOUR_CARDS. On a wide screen with
  // nothing pinned it sits beside the card; otherwise it goes above the card,
  // because a third column would be too cramped.
  const hourHere = HOUR_CARDS.includes(open);
  const hourBeside = hourHere && wide && !phone && !pinShown;

  // The j/k key listener. useLayoutEffect (not useEffect) attaches it in the
  // same render that draws the card, so a key pressed right away is not missed.
  // Keys are ignored while focus is in a text field or an open dialog.
  useLayoutEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey) return;
      const target = event.target instanceof Element ? event.target : null;
      if (target?.closest('input, textarea, select, [contenteditable="true"], dialog[open]'))
        return;
      if (event.key === 'j') onOpen(neighbours(open).next);
      else if (event.key === 'k') onOpen(neighbours(open).previous);
      else return;
      event.preventDefault();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onOpen]);

  // #region bench-drawer
  // The drawer is a native <dialog>, like the site's own SideNav drawer.
  // The Cards button sets `drawerOpen`; this effect opens or closes the dialog
  // to match.
  useEffect(() => {
    const drawer = drawerRef.current;
    if (!drawer) return;
    if (drawerOpen && !drawer.open) drawer.showModal();
    if (!drawerOpen && drawer.open) drawer.close();
  }, [drawerOpen, railInDrawer]);
  const openFromDrawer = (slug: CardSlug) => {
    setDrawerOpen(false);
    onOpen(slug);
  };
  // #endregion bench-drawer

  return (
    <div className={styles.bench} data-testid="workbench">
      {railInDrawer ? (
        <dialog
          ref={drawerRef}
          className={styles.benchDrawer}
          aria-label="Admin cards"
          data-testid="bench-drawer"
          onClose={() => setDrawerOpen(false)}
          onClick={(event) => {
            // Native dialog: a click on the backdrop targets the dialog itself.
            if (event.target === drawerRef.current) setDrawerOpen(false);
          }}
        >
          {drawerOpen && (
            <nav className={styles.drawerRail} aria-label="Admin cards" data-testid="bench-rail">
              <p className={styles.drawerHead}>
                <span className={styles.pinnedLabel}>Cards</span>
                <button
                  type="button"
                  className={cardStyles.back}
                  onClick={() => setDrawerOpen(false)}
                  data-testid="bench-drawer-close"
                >
                  Close
                </button>
              </p>
              <AdminRailContent open={open} asked={null} onOpen={openFromDrawer} />
            </nav>
          )}
        </dialog>
      ) : (
        <nav
          className={`${styles.rail} op-glass op-rail`}
          aria-label="Admin cards"
          data-testid="bench-rail"
        >
          <AdminRailContent open={open} asked={asked} onOpen={onOpen} />
        </nav>
      )}
      <div className={styles.benchMain}>
        {railInDrawer && asked !== null && (
          <p className={styles.railNote} role="status" data-testid="bench-unknown">
            No card is called &lsquo;{asked}&rsquo;, so this is {openCard.name}.
          </p>
        )}
        <div className={styles.benchBar}>
          {railInDrawer && (
            <button
              type="button"
              className={cardStyles.back}
              aria-haspopup="dialog"
              aria-expanded={drawerOpen}
              onClick={() => setDrawerOpen(true)}
              data-testid="bench-cards"
            >
              Cards
            </button>
          )}
          <p className={styles.crumb} data-testid="bench-crumb">
            {question?.title} <span aria-hidden="true">/</span> {openCard.name}
          </p>
          <div className={styles.benchControls}>
            {/* Pin is a toggle. Its label stays "Pin"; aria-pressed and the dot
                show its state, so a screen reader says "Pin, pressed" rather
                than the confusing "Pinned, pressed". */}
            <button
              type="button"
              className={`${cardStyles.back} ${styles.pinButton}`}
              aria-pressed={pin === open}
              onClick={() => onPin(pin === open ? null : open)}
              data-testid="bench-pin"
            >
              <span className={styles.pinDot} aria-hidden="true" />
              Pin
            </button>
            <p
              className={`${styles.stepper} op-seg`}
              role="group"
              aria-label="The card before and after"
            >
              <button
                type="button"
                className={cardStyles.back}
                onClick={() => onOpen(previous)}
                aria-label={`Previous card, ${benchCard(previous).name}`}
                data-testid="bench-previous"
              >
                Previous
              </button>
              <button
                type="button"
                className={cardStyles.back}
                onClick={() => onOpen(next)}
                aria-label={`Next card, ${benchCard(next).name}`}
                data-testid="bench-next"
              >
                Next
              </button>
            </p>
          </div>
        </div>
        {hourHere && !hourBeside && <AdminHourAtAGlance glance={glance} beside={false} />}
        <div
          className={styles.benchColumns}
          data-pinned={pinShown && !phone ? 'true' : 'false'}
          data-hour={!hourHere ? 'none' : hourBeside ? 'beside' : 'above'}
        >
          <div className={styles.benchColumn} data-testid="bench-open" data-card={open}>
            {render(open)}
          </div>
          {pinShown && !phone && (
            <div className={styles.benchColumn} data-testid="bench-pinned" data-card={pin}>
              <p className={styles.columnBar}>
                <span className={styles.pinnedLabel}>Pinned beside it</span>
                <button
                  type="button"
                  className={cardStyles.back}
                  onClick={() => onPin(null)}
                  data-testid="bench-unpin"
                >
                  Unpin
                </button>
              </p>
              {render(pin)}
            </div>
          )}
          {pinShown && phone && (
            <details
              className={styles.pinFold}
              data-testid="bench-pinned"
              data-card={pin}
              open={foldOpen}
              onToggle={(event) => setFoldOpen(event.currentTarget.open)}
            >
              <summary className={styles.pinFoldSummary} data-testid="bench-pin-fold">
                Pinned: {benchCard(pin).name}
              </summary>
              {foldOpen && (
                <div className={styles.benchColumn}>
                  <p className={styles.columnBar}>
                    <button
                      type="button"
                      className={cardStyles.back}
                      onClick={() => onPin(null)}
                      data-testid="bench-unpin"
                    >
                      Unpin
                    </button>
                  </p>
                  {render(pin)}
                </div>
              )}
            </details>
          )}
          {hourBeside && <AdminHourAtAGlance glance={glance} beside />}
        </div>
      </div>
    </div>
  );
}

/** Cards that show "This hour": the Admin home and the two cards it sums up. */
const HOUR_CARDS: readonly CardSlug[] = ['health', 'timing', 'traffic'];
// #endregion workbench
