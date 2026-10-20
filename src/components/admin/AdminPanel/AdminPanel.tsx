// The Admin tab: the running site reporting on itself (ADR-010, ADR-080).
//
// AdminPanel, the export, is the page. It holds the operator's key, takes the shared reads,
// builds the tiles and lays the parts out. Each part is a file of its own:
//   1. admin/AdminRailContent    - the cards grouped by question, with a search box.
//   2. admin/AdminWorkbench      - the layout: rail, open card, pinned card, j/k keys.
//   3. admin/AdminHourAtAGlance  - the "This hour" rings and counts shown with some cards.
//   4. admin/AdminStatStrip      - the row of tiles across the top.
//   5. ./useMachines.tsx         - the hook that reads machine stats for charts and tiles.
// Beside them: admin/AdminBenchCard draws one card and loads its code, ./useAdminReads.ts makes
// the strip's reads, ./stripTiles.ts turns readings into tiles, and ./followCardLink.ts is the
// click rule every card link follows.
import { type ReactNode, useEffect, useState } from 'react';
import { useMediaQuery } from '../../../hooks/useMediaQuery';
import { PHONE } from '../../../lib/breakpoints';
import { adminKey, browserStorage, forgetAdminKey, rememberAdminKey } from '../../../lib/adminKey';
import type { CardSlug } from '../../../lib/workbench';
import { prefetchWhenIdle } from '../../../lib/prefetch';
import styles from './AdminPanel.module.css';
import cardStyles from '../shared/card.module.css';
import { About } from '../shared/common';
import { AdminBenchCard, CARD_CHUNKS } from '../AdminBenchCard';
import { AdminStatStrip } from '../AdminStatStrip';
import { AdminWorkbench } from '../AdminWorkbench';
import { useAdminReads } from './useAdminReads';
import { useCloudGlances } from './useCloudGlances';
import { useMachines } from './useMachines';
import { stripTiles } from './stripTiles';

/**
 * The operator's key. It comes from the address bar, or else from what this
 * browser saved earlier (src/lib/adminKey.ts).
 *
 * We read it once, when the module loads, not on every render. On its first
 * render the app rewrites the address bar and drops anything it did not put
 * there, including the key. That is good (a key left in the URL ends up in
 * browser history), but it means the key must be read before that happens.
 * Saving it lets the operator open the page later, on another device, without
 * pasting the key again.
 */
const ADMIN_KEY = adminKey();

/**
 * The Admin tab (ADR-010). Each card fetches and fails on its own, so one
 * broken source (say, Azure) never hides the rest. The tab is public on
 * purpose; ADR-010 explains why.
 */
export function AdminPanel({
  onBack,
  backTo = 'inventory',
  signedIn,
  onOpenAccount,
  card = 'overview',
  pin = null,
  cardAsked = null,
  onOpenCard = () => {},
  onPin = () => {},
}: {
  onBack: () => void;
  /** Where the back button goes. Its label names the same place. */
  backTo?: 'home' | 'inventory';
  signedIn: boolean;
  onOpenAccount: () => void;
  /** The card named in the address, and the card pinned beside it, if any. */
  card?: CardSlug;
  pin?: CardSlug | null;
  /** A card name from the address that matched no card, so the rail can say so. */
  cardAsked?: string | null;
  onOpenCard?: (slug: CardSlug) => void;
  onPin?: (slug: CardSlug | null) => void;
}) {
  // Every card's chunk, fetched once the tab is up and the browser is idle.
  useEffect(() => prefetchWhenIdle(Object.values(CARD_CHUNKS)), []);
  // The key is state so that forgetting it updates the cards at once.
  // It starts as the value read when the module loaded.
  const [adminKey, setAdminKey] = useState<string | null>(ADMIN_KEY);
  const forgetKey = () => {
    forgetAdminKey(browserStorage());
    setAdminKey(null);
  };
  const enterKey = (entered: string) => {
    const key = rememberAdminKey(entered, browserStorage());
    if (key !== null) setAdminKey(key);
  };

  const reads = useAdminReads(); // ./useAdminReads.ts: tick, health, errors, pages, visitors
  const machines = useMachines(); // ./useMachines.tsx: machine stats and the chart window
  const glances = useCloudGlances(reads.tick); // ./useCloudGlances.ts: the rail's tone and headline per cloud card
  const { tiles, glance, caption } = stripTiles({
    seen: machines.latest,
    window: machines.window,
    health: reads.health,
    errors: reads.errors,
    pagesSeen: reads.pagesSeen,
    visitorsToday: reads.visitorsToday,
  }); // ./stripTiles.ts: the tiles, the hour's summary and the caption

  // One card by its slug, for the open column and the pinned one (admin/AdminBenchCard).
  const renderCard = (slug: CardSlug): ReactNode => (
    <AdminBenchCard
      key={slug}
      slug={slug}
      tick={reads.tick}
      health={reads.health}
      tiles={tiles}
      errors={reads.errors}
      machines={machines.machines}
      window={machines.window}
      toolbar={machines.toolbar}
      signedIn={signedIn}
      onOpenAccount={onOpenAccount}
      rowsServed={reads.rowsServed}
      adminKey={adminKey}
      onActivity={reads.onActivity}
      onPagesReport={reads.setPagesSeen}
      onEnterKey={enterKey}
      onForgetKey={forgetKey}
    />
  );

  // #region admin-panel-order
  // On a phone the words follow the tiles in the DOM itself, not by CSS order, so a keyboard
  // reaches the tiles first the way the eye does (WCAG 1.3.2 and 2.4.3; ADR-080, the addendum
  // on the reading order). A server draws the desk order and the browser moves the block once.
  const phone = useMediaQuery(PHONE);
  const intro = (
    <div className={styles.intro} data-testid="admin-intro">
      <p className={styles.blurb}>
        The running system reporting on itself, in the order somebody asks: is it up, is it fast, is
        it costing anything, what broke.
      </p>
      <About>
        One card at a time, the one the address names: the rail lists every card under the question
        it answers, a tile opens the card behind its number, j and k walk the rail, and a pinned
        card stays beside whichever one is open. Every statistic the page shows for one store it
        shows for the other, and where a number has no meaning on one side the page says so in
        words. Refreshes every 30 seconds. Public on purpose; the reasoning is in the Best Practices
        menu.
      </About>
    </div>
  );
  // #endregion admin-panel-order
  return (
    <section className={styles.wrap} aria-label="Admin">
      <div className={styles.head}>
        <h1 className={styles.title}>Admin</h1>
        <button type="button" className={cardStyles.back} onClick={onBack}>
          {backTo === 'home' ? 'Back to home' : 'Back to inventory'}
        </button>
      </div>
      {phone ? null : intro}
      <AdminStatStrip
        tiles={tiles}
        onOpenCard={onOpenCard}
        toolbar={machines.toolbar('over the tiles', 'strip-window')}
        caption={caption}
      />
      {phone ? intro : null}

      <div className={styles.benchOrder}>
        <AdminWorkbench
          open={card}
          pin={pin}
          asked={cardAsked}
          onOpen={onOpenCard}
          onPin={onPin}
          glance={glance}
          glances={glances}
          render={renderCard}
        />
      </div>
    </section>
  );
}
