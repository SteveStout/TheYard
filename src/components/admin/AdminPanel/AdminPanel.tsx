// The Admin tab: the running site reporting on itself (ADR-010, ADR-080).
//
// AdminPanel, the export, is the page: it reads shared data, builds the
// tiles and picks which card to show. The cards live in ./admin/, one file
// each. Below AdminPanel come the five parts it uses, in file order:
//   1. RailContent    - the list of cards grouped by question, with a search box.
//   2. Workbench      - the layout: rail, open card, pinned card, j/k keys.
//   3. HourAtAGlance  - the "This hour" rings and counts shown with some cards.
//   4. StatStrip      - the row of tiles across the top.
//   5. useMachines    - the hook that reads machine stats for charts and tiles.
import {
  lazy,
  Suspense,
  type MouseEvent,
  type ReactNode,
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
} from 'react';
import type { ActivityReport } from '../../../lib/activity';
import { adminKey, browserStorage, forgetAdminKey, rememberAdminKey } from '../../../lib/adminKey';
import {
  clockLabel,
  fromFirstReading,
  keptSparks,
  MACHINE_WINDOWS,
  type MachineWindow,
  timeline,
  trafficTotals,
  windowName,
} from '../../../lib/machineChart';
import {
  afterColdStart,
  hourTiming,
  sparkCaption,
  sparkRuns,
  type StatTile,
  tileSentence,
  tilesFrom,
  type TileQuestion,
  visitorsOn,
} from '../../../lib/statTiles';
import {
  BENCH_QUESTIONS,
  benchCard,
  cardForTile,
  findCards,
  hourGlance,
  neighbours,
  type HourGlance,
} from '../../../lib/bench';
import type { CardSlug } from '../../../lib/workbench';
import { prefetchWhenIdle } from '../../../lib/prefetch';
import { useMediaQuery } from '../../../hooks/useMediaQuery';
import { PHONE, WIDE, WIDEST } from '../../../lib/breakpoints';
import styles from './AdminPanel.module.css';
import cardStyles from '../shared/card.module.css';
import stripStyles from '../shared/stat-strip.module.css';
import { Readout } from '../../shared/Readout';
import { Ring } from '../../shared/Ring';
import type { ErrorEntry, Fetched, Health, Machines, PageStatus } from '../shared/types';
import { About, Absent, hourSlots, REFRESH_MS } from '../shared/common';

// #region lazy-cards
// Each card is its own code chunk. `lazy` means the card's code is not in the
// Admin tab's own chunk, so the tab itself stays small: just the strip, the
// rail and the strip's reads. Once the tab has mounted and the browser is
// idle, every card's chunk is fetched ahead of the click (src/lib/prefetch.ts),
// so choosing a card waits on its data and not on its code.
const CARD_CHUNKS = {
  HealthCard: () => import('../HealthCard/HealthCard'),
  AzureCard: () => import('../AzureCard/AzureCard'),
  PagesCard: () => import('../PagesCard/PagesCard'),
  TestsCard: () => import('../TestsCard/TestsCard'),
  TrafficSection: () => import('../TrafficCard/TrafficCard'),
  TelemetryCard: () => import('../TelemetryCard/TelemetryCard'),
  TimingCard: () => import('../TimingCard/TimingCard'),
  BackendsCard: () => import('../BackendsCard/BackendsCard'),
  ProofCard: () => import('../ProofCard/ProofCard'),
  MachinesCard: () => import('../MachinesCard/MachinesCard'),
  ExperimentCard: () => import('../ExperimentCard/ExperimentCard'),
  SqlCard: () => import('../SqlCard/SqlCard'),
  StoreCard: () => import('../StoreCard/StoreCard'),
  ErrorsCard: () => import('../ErrorsCard/ErrorsCard'),
  LogCard: () => import('../LogCard/LogCard'),
  KeptLogsCard: () => import('../KeptLogsCard/KeptLogsCard'),
  ActivityCard: () => import('../ActivityCard/ActivityCard'),
  OperatorCard: () => import('../OperatorCard/OperatorCard'),
  ResetLinkCard: () => import('../ResetLinkCard/ResetLinkCard'),
};
const HealthCard = lazy(CARD_CHUNKS.HealthCard);
const AzureCard = lazy(CARD_CHUNKS.AzureCard);
const PagesCard = lazy(CARD_CHUNKS.PagesCard);
const TestsCard = lazy(CARD_CHUNKS.TestsCard);
const TrafficSection = lazy(CARD_CHUNKS.TrafficSection);
const TelemetryCard = lazy(CARD_CHUNKS.TelemetryCard);
const TimingCard = lazy(CARD_CHUNKS.TimingCard);
const BackendsCard = lazy(CARD_CHUNKS.BackendsCard);
const ProofCard = lazy(CARD_CHUNKS.ProofCard);
const MachinesCard = lazy(CARD_CHUNKS.MachinesCard);
const ExperimentCard = lazy(CARD_CHUNKS.ExperimentCard);
const SqlCard = lazy(CARD_CHUNKS.SqlCard);
const StoreCard = lazy(CARD_CHUNKS.StoreCard);
const ErrorsCard = lazy(CARD_CHUNKS.ErrorsCard);
const LogCard = lazy(CARD_CHUNKS.LogCard);
const KeptLogsCard = lazy(CARD_CHUNKS.KeptLogsCard);
const ActivityCard = lazy(CARD_CHUNKS.ActivityCard);
const OperatorCard = lazy(CARD_CHUNKS.OperatorCard);
const ResetLinkCard = lazy(CARD_CHUNKS.ResetLinkCard);
// #endregion lazy-cards

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
  card = 'health',
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
  // Whether this site serves per-visitor rows (ADR-071). The activity report
  // tells us; null until it answers.
  const [rowsServed, setRowsServed] = useState<boolean | null>(null);
  // Two numbers the tiles need that other reads supply: today's visitors
  // (from the activity report) and the last check of every page.
  const [visitorsToday, setVisitorsToday] = useState<number | null>(null);
  const [pagesSeen, setPagesSeen] = useState<{ checked: number; up: number } | null>(null);
  const onActivity = useCallback((report: ActivityReport) => {
    setRowsServed(report.visitor_rows);
    setVisitorsToday(visitorsOn(report.days, new Date()));
  }, []);
  const {
    machines,
    latest: latestMachines,
    window: machineWindow,
    toolbar: windowToolbar,
  } = useMachines();
  const [health, setHealth] = useState<Fetched<Health>>(null);
  const [errors, setErrors] = useState<Fetched<ErrorEntry[]>>(null);
  const [tick, setTick] = useState(0);

  useEffect(() => {
    // Bump `tick` every REFRESH_MS. The strip and the open card re-read when
    // it changes. Skip it while the browser tab is hidden: nobody is looking.
    const id = window.setInterval(() => {
      if (!document.hidden) setTick((t) => t + 1);
    }, REFRESH_MS);
    return () => window.clearInterval(id);
  }, []);

  useEffect(() => {
    let live = true;
    // The strip's own reads: health, recent errors and the page check.
    // Cards read their own data, and only while open.
    // `live` goes false on cleanup, so a late answer never sets stale state.
    // A network error or non-200 answer becomes 'failed', not endless loading.
    const grab = <T,>(url: string, set: (v: Fetched<T>) => void) =>
      fetch(url)
        .then((r) =>
          r.ok ? (r.json() as Promise<T>) : Promise.reject(new Error(String(r.status)))
        )
        .then((v) => {
          if (live) set(v);
        })
        .catch(() => {
          if (live) set('failed');
        });
    void grab<Health>('/api/health', setHealth);
    void grab<ErrorEntry[]>('/api/errors', setErrors);
    // The strip reads the page check itself, because the Pages card that also
    // reads it is only mounted while it is open.
    void grab<PageStatus>('/api/admin/pages', (answer) => {
      if (answer !== null && answer !== 'failed' && answer.report !== null) {
        setPagesSeen({ checked: answer.report.checked, up: answer.report.up });
      }
    });
    return () => {
      live = false;
    };
  }, [tick]);

  // Read the activity report once when the tab opens, for today's visitors
  // and for `rowsServed`. The Activity card, when open, does its own read and
  // passes the result back through onActivity.
  useEffect(() => {
    let live = true;
    void fetch('/api/admin/activity?window=7d')
      .then((r) =>
        r.ok ? (r.json() as Promise<ActivityReport>) : Promise.reject(new Error(String(r.status)))
      )
      .then((report) => {
        if (live) onActivity(report);
      })
      .catch(() => {});
    return () => {
      live = false;
    };
  }, [onActivity]);

  // #region tiles
  // Build the tiles from data already read above, using the rules in
  // statTiles.ts. Because the strip and the cards share the same data, a tile
  // can never disagree with the card it links to.
  const seen = latestMachines;
  const hour = seen === null ? null : hourSlots(seen);
  // The small line charts (sparklines) under the tiles use the chart window
  // the user picked. For any window longer than 1h, use the stored history,
  // but only once it has arrived for that window and the store keeps it.
  const keptHistory =
    seen !== null &&
    machineWindow !== '1h' &&
    seen.history !== undefined &&
    seen.history.window === machineWindow &&
    seen.history.available
      ? seen.history
      : null;
  const keptLines =
    keptHistory === null || machineWindow === '1h'
      ? null
      : keptSparks(
          fromFirstReading(
            timeline(
              keptHistory.buckets,
              machineWindow,
              keptHistory.bucket_minutes,
              new Date(keptHistory.as_of)
            )
          )
        );
  const lastSample =
    seen !== null && seen.container.samples.length > 0
      ? seen.container.samples[seen.container.samples.length - 1]
      : null;
  // A cold start (the slow first minutes after the process starts) should not
  // count against the hour's speed (see statTiles.ts). Start time is the newest
  // sample's time minus uptime. Both come from the server's answer, so this
  // code never reads the browser's clock.
  const startedAt =
    seen === null || lastSample === null
      ? null
      : new Date(new Date(lastSample.at).getTime() - seen.container.uptime_seconds * 1000);
  const warmed = hour === null ? null : afterColdStart(hour, startedAt);
  // Request and error counts still cover the whole hour. Only the timings
  // (p50 is the median, p95 the 95th percentile) skip the cold-start minutes.
  const warmTiming = warmed === null ? null : hourTiming(warmed.warm);
  const hourTotals =
    hour === null || warmTiming === null
      ? null
      : {
          ...trafficTotals(hour, 1),
          warm_requests: warmTiming.requests,
          p50_ms: warmTiming.p50_ms,
          p95_ms: warmTiming.p95_ms,
          slowest_at: warmTiming.slowest_at,
        };
  const coldStart =
    warmed !== null && warmed.left_out.some((slot) => (slot.requests ?? 0) > 0)
      ? clockLabel(warmed.left_out[0].at)
      : null;
  const tiles = tilesFrom({
    health: health !== null && health !== 'failed' ? health : null,
    pages: pagesSeen,
    traffic:
      hourTotals === null
        ? null
        : {
            ...hourTotals,
            slowest_label:
              hourTotals.slowest_at === null ? null : clockLabel(hourTotals.slowest_at),
            cold_start_label: coldStart,
          },
    memory:
      seen === null || lastSample === null
        ? null
        : { working_set_mb: lastSample.working_set_mb, limit_mb: seen.container.memory_limit_mb },
    charged:
      seen === null
        ? null
        : seen.document.available
          ? {
              request_units: seen.document.request_units,
              free_per_second: seen.document.free_request_units_per_second,
            }
          : 'none',
    errors: errors !== null && errors !== 'failed' ? errors.length : null,
    visitorsToday,
    sparks:
      seen === null
        ? undefined
        : keptLines !== null
          ? { ...keptLines, charged: seen.document.available ? keptLines.charged : undefined }
          : {
              speed: hour?.map((slot) => slot.p50_ms),
              memory: seen.container.samples.map((sample) => sample.working_set_mb),
              charged: seen.document.available
                ? seen.document.minutes.map((minute) => minute.request_units)
                : undefined,
              errors: hour?.map((slot) => slot.server_errors),
            },
  });
  // #endregion tiles
  const glance = hourGlance(
    hourTotals === null
      ? null
      : {
          requests: hourTotals.requests,
          server_errors: hourTotals.server_errors,
          client_errors: hourTotals.client_errors,
          p50_ms: hourTotals.p50_ms,
          p95_ms: hourTotals.p95_ms,
        }
  );

  // #region bench-cards
  // Draw one card by its slug (ADR-080). A card's code loads the first time
  // it is opened or pinned, and it reads data only while on the page. Data the
  // strip already read (health, errors, machines) is passed in, not re-read.
  // Suspense shows CardLoading while the card's code downloads.
  const renderCard = (slug: CardSlug): ReactNode => {
    const drawn = (() => {
      switch (slug) {
        case 'health':
          return <HealthCard health={health} />;
        case 'azure':
          return <AzureCard tick={tick} />;
        case 'pages':
          return <PagesCard onReport={setPagesSeen} />;
        case 'tests':
          return <TestsCard />;
        case 'traffic':
          return (
            <TrafficSection
              machines={machines}
              window={machineWindow}
              toolbar={windowToolbar('on the traffic card', 'machines-window')}
            />
          );
        case 'telemetry':
          return <TelemetryCard tick={tick} />;
        case 'timing':
          return <TimingCard tick={tick} />;
        case 'backends':
          return <BackendsCard tick={tick} />;
        case 'proof':
          return <ProofCard tick={tick} signedIn={signedIn} onOpenAccount={onOpenAccount} />;
        case 'machines':
          return (
            <MachinesCard
              machines={machines}
              window={machineWindow}
              toolbar={windowToolbar('on the machines card', 'machines-card-window')}
            />
          );
        case 'experiment':
          return <ExperimentCard tick={tick} />;
        case 'sql':
          return <SqlCard tick={tick} />;
        case 'store':
          return <StoreCard tick={tick} />;
        case 'errors':
          return <ErrorsCard errors={errors} tick={tick} />;
        case 'log':
          return <LogCard tick={tick} />;
        case 'kept':
          return rowsServed === true ? (
            <KeptLogsCard adminKey={adminKey} />
          ) : (
            <Absent
              name={benchCard('kept').name}
              note={
                rowsServed === null
                  ? null
                  : 'This site does not serve its kept rows, so there is no kept log to read here.'
              }
            />
          );
        case 'activity':
          return <ActivityCard adminKey={adminKey} rowsServed={rowsServed} onReport={onActivity} />;
        case 'operator':
          return <OperatorCard adminKey={adminKey} onEnterKey={enterKey} onForget={forgetKey} />;
        case 'reset':
          return <ResetLinkCard adminKey={adminKey} />;
      }
    })();
    return (
      <Suspense key={slug} fallback={<CardLoading slug={slug} />}>
        {drawn}
      </Suspense>
    );
  };
  // #endregion bench-cards

  return (
    <section className={styles.wrap} aria-label="Admin">
      <div className={styles.head}>
        <h1 className={styles.title}>Admin</h1>
        <button type="button" className={cardStyles.back} onClick={onBack}>
          {backTo === 'home' ? 'Back to home' : 'Back to inventory'}
        </button>
      </div>
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
      <StatStrip
        tiles={tiles}
        onOpenCard={onOpenCard}
        toolbar={windowToolbar('over the tiles', 'strip-window')}
        caption={sparkCaption(
          windowName(machineWindow).toLowerCase(),
          machineWindow === '1h'
            ? 'hour'
            : keptLines !== null
              ? 'kept'
              : latestMachines?.history?.window === machineWindow
                ? 'not-kept'
                : 'reading'
        )}
      />

      <Workbench
        open={card}
        pin={pin}
        asked={cardAsked}
        onOpen={onOpenCard}
        onPin={onPin}
        glance={glance}
        render={renderCard}
      />
    </section>
  );
}

/**
 * Placeholder while a card's code downloads: the card's name, at the height
 * of a real card, so the content below does not jump when the card arrives.
 */
function CardLoading({ slug }: { slug: CardSlug }) {
  return (
    <article
      className={`${cardStyles.wide} ${styles.cardLoading} op-glass`}
      data-testid="bench-loading"
    >
      <h2 className={cardStyles.cardTitle}>{benchCard(slug).name}</h2>
      <p className={cardStyles.muted} role="status">
        Loading…
      </p>
    </article>
  );
}

// #region workbench
/**
 * Click handler for card links. A plain left click opens the card in place.
 * A click with a modifier key, or the middle button, means "open in a new tab",
 * so we leave it to the browser, which follows the link's href.
 */
function follow(event: MouseEvent<HTMLElement>, open: () => void) {
  if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
    return;
  }
  event.preventDefault();
  open();
}

/**
 * What goes in the rail (the side list of cards): a search box, a note when the
 * address named an unknown card, and the cards grouped under five questions.
 * On a wide screen the rail sits beside the card; on narrower ones it is in the
 * Cards drawer.
 */
function RailContent({
  open,
  asked,
  onOpen,
}: {
  open: CardSlug;
  asked: string | null;
  onOpen: (slug: CardSlug) => void;
}) {
  const [query, setQuery] = useState('');
  const found = findCards(query);
  const openCard = benchCard(open);
  return (
    <>
      <label className={styles.railFind}>
        <span className={styles.railFindLabel}>Find a card</span>
        <input
          type="search"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="timing, errors, sql"
          data-testid="bench-find"
        />
      </label>
      {asked !== null && (
        <p className={styles.railNote} role="status" data-testid="bench-unknown">
          No card is called &lsquo;{asked}&rsquo;, so this is {openCard.name}.
        </p>
      )}
      {BENCH_QUESTIONS.map((entry, index) => {
        const cards = found.filter((candidate) => candidate.question === entry.key);
        if (cards.length === 0) return null;
        return (
          <section
            key={entry.key}
            className={styles.railGroup}
            aria-labelledby={`question-${entry.key}`}
            data-testid={`question-${entry.key}`}
          >
            <h2 className={styles.railTitle} id={`question-${entry.key}`}>
              <span>
                {String(index + 1).padStart(2, '0')} {entry.title}
              </span>
              <span className={styles.railCount}>{cards.length}</span>
            </h2>
            <ul className={styles.railList}>
              {cards.map((candidate) => (
                <li key={candidate.slug}>
                  <a
                    href={`?view=admin&card=${candidate.slug}`}
                    className={styles.railLink}
                    aria-current={candidate.slug === open ? 'page' : undefined}
                    data-testid={`bench-link-${candidate.slug}`}
                    onClick={(event) => follow(event, () => onOpen(candidate.slug))}
                  >
                    {candidate.name}
                  </a>
                </li>
              ))}
            </ul>
          </section>
        );
      })}
      {found.length === 0 && (
        <p className={styles.railNote} data-testid="bench-none">
          No card matches &lsquo;{query}&rsquo;.
        </p>
      )}
    </>
  );
}

/**
 * The workbench layout (ADR-080): the rail on the left, the open card large
 * beside it, and an optional second column for a pinned card.
 * The j and k keys move to the next and previous card, unless you are typing.
 * Under 1280px the rail moves into a drawer behind a Cards button. Under 640px
 * the pinned card becomes a fold under the open one and reads nothing until
 * you expand it.
 */
function Workbench({
  open,
  pin,
  asked,
  onOpen,
  onPin,
  glance,
  render,
}: {
  open: CardSlug;
  pin: CardSlug | null;
  asked: string | null;
  onOpen: (slug: CardSlug) => void;
  onPin: (slug: CardSlug | null) => void;
  glance: HourGlance;
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
              <RailContent open={open} asked={null} onOpen={openFromDrawer} />
            </nav>
          )}
        </dialog>
      ) : (
        <nav
          className={`${styles.rail} op-glass op-rail`}
          aria-label="Admin cards"
          data-testid="bench-rail"
        >
          <RailContent open={open} asked={asked} onOpen={onOpen} />
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
        {hourHere && !hourBeside && <HourAtAGlance glance={glance} beside={false} />}
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
          {hourBeside && <HourAtAGlance glance={glance} beside />}
        </div>
      </div>
    </div>
  );
}

/** Cards that show "This hour": the Admin home and the two cards it sums up. */
const HOUR_CARDS: readonly CardSlug[] = ['health', 'timing', 'traffic'];

/**
 * The "This hour" panel: two rings for response time (p95 outside, p50
 * inside, both against the same scale), the number in words in the middle,
 * and the hour's counts below.
 * It uses the same data as the tiles, so the two always agree.
 */
function HourAtAGlance({ glance, beside }: { glance: HourGlance; beside: boolean }) {
  return (
    <section
      className={`${beside ? styles.hourBeside : styles.hourAbove} op-glass`}
      aria-labelledby="bench-hour-title"
      data-testid="bench-hour"
    >
      <h2 className={styles.hourTitle} id="bench-hour-title">
        This hour
      </h2>
      <div className={styles.hourBody}>
        <Ring
          value={glance.ring?.p95 ?? 0}
          max={glance.ring?.max ?? 1}
          second={{ value: glance.ring?.p50 ?? 0, max: glance.ring?.max ?? 1 }}
          size={beside ? 'large' : 'page'}
          inside={glance.inside}
          label={glance.label}
          testId="bench-hour-ring"
          graduated
        />
        <Readout rows={glance.rows} testId="bench-hour-readout" />
      </div>
    </section>
  );
}
// #endregion workbench

// #region stat-strip
/**
 * The strip of tiles across the top (ADR-080). Each tile is a link to the
 * card that explains it. statTiles.ts (plain TypeScript, no React) decides
 * what a tile says and its tone; this code only draws. The tone is shown as a
 * word as well as a colour, so it does not depend on seeing colour.
 */
const QUESTION_ORDER: TileQuestion[] = ['up', 'fast', 'cost', 'broke'];

const TONE_WORD: Record<StatTile['tone'], string | null> = {
  good: 'fine',
  warn: 'worth a look',
  bad: 'needs attention',
  plain: null,
  waiting: 'waiting',
};

function StatStrip({
  tiles,
  onOpenCard,
  toolbar,
  caption,
}: {
  tiles: StatTile[];
  /** Opens the card a tile links to. */
  onOpenCard: (slug: CardSlug) => void;
  toolbar: ReactNode;
  caption: string;
}) {
  const toneClass: Record<StatTile['tone'], string> = {
    good: stripStyles.tileGood,
    warn: stripStyles.tileWarn,
    bad: stripStyles.tileBad,
    plain: stripStyles.tilePlain,
    waiting: stripStyles.tileWaiting,
  };
  const ordered = QUESTION_ORDER.flatMap((question) =>
    tiles.filter((tile) => tile.question === question)
  );
  return (
    <>
      <div className={stripStyles.stripHead}>
        {toolbar}
        <p className={cardStyles.muted} data-testid="strip-caption">
          {caption}
        </p>
      </div>
      <ul
        className={`${stripStyles.strip} ${stripStyles.statStrip}`}
        aria-label="The site at a glance"
        data-testid="stat-strip"
      >
        {ordered.map((tile) => {
          const runs = tile.spark === undefined ? [] : sparkRuns(tile.spark, 100, 24);
          const word = TONE_WORD[tile.tone];
          return (
            <li key={tile.key} className={stripStyles.stripItem}>
              <a
                href={`?view=admin&card=${cardForTile(tile.key)}`}
                className={`${stripStyles.tile} op-glass op-tile ${toneClass[tile.tone]}`}
                data-testid={`tile-${tile.key}`}
                data-tone={tile.tone}
                onClick={(event) => follow(event, () => onOpenCard(cardForTile(tile.key)))}
              >
                {/* A tile is: label, value row (number plus optional ring), detail,
                    tone word, sparkline. Numbers line up on one baseline per row. */}
                <span className={stripStyles.tileLabel}>{tile.label}</span>
                <span className={stripStyles.tileValueRow}>
                  <span className={stripStyles.tileValue}>{tile.value}</span>
                  {/* The ring shows a share of a whole (ADR-081). Screen readers skip
                      it, since the tile says the number in words. Its box is there
                      before data arrives, so the number does not shift when it does. */}
                  {tile.ringed && (
                    <span className={stripStyles.tileRing}>
                      {tile.ring !== undefined && (
                        <Ring
                          value={tile.ring.share}
                          max={1}
                          inside={tile.ring.label}
                          label={null}
                          testId="tile-ring"
                        />
                      )}
                    </span>
                  )}
                </span>
                {/* The detail is kept short enough for two lines. The full sentence is
                    in the title (hover text), and the extra part is read to screen readers. */}
                <span className={stripStyles.tileDetail} title={tileSentence(tile)}>
                  {tile.detail}
                  {tile.more !== undefined && <span className={styles.srOnly}>{tile.more}</span>}
                </span>
                {/* Empty slots hold the space for the tone word and sparkline, so
                    the page does not jump when the data arrives. */}
                {word !== null ? (
                  <span className={stripStyles.tileTone}>{word}</span>
                ) : (
                  <span className={stripStyles.tileToneSlot} aria-hidden="true" />
                )}
                {runs.length === 0 && <span className={stripStyles.sparkSlot} aria-hidden="true" />}
                {runs.length > 0 && (
                  <svg
                    className={stripStyles.spark}
                    viewBox="0 0 100 24"
                    preserveAspectRatio="none"
                    aria-hidden="true"
                    focusable="false"
                  >
                    {runs.map((points) => (
                      <polyline key={points} points={points} />
                    ))}
                  </svg>
                )}
              </a>
            </li>
          );
        })}
      </ul>
    </>
  );
}
// #endregion stat-strip

// #region machines-read
/**
 * Reads machine stats once and shares them with the Traffic card, the Machines
 * card and the tiles. All three use one time window, so they always cover the
 * same stretch and can be compared. It is a hook, not a component, because the
 * two cards live in different places on the page.
 */
function useMachines(): {
  machines: Fetched<Machines>;
  latest: Machines | null;
  window: MachineWindow;
  toolbar: (where: string, testPrefix: string) => ReactNode;
} {
  const [machines, setMachines] = useState<Fetched<Machines>>(null);
  // The last good answer. Unlike `machines`, it is not cleared when the window
  // changes, so the tiles keep their values instead of flashing "waiting".
  const [latest, setLatest] = useState<Machines | null>(null);
  const [window_, setWindow] = useState<MachineWindow>('1h');

  useEffect(() => {
    let live = true;
    const read = () =>
      fetch(window_ === '1h' ? '/api/admin/machines' : `/api/admin/machines?window=${window_}`)
        .then((r) =>
          r.ok ? (r.json() as Promise<Machines>) : Promise.reject(new Error(String(r.status)))
        )
        .then((v) => {
          if (!live) return;
          setMachines(v);
          setLatest(v);
        })
        .catch(() => {
          if (live) setMachines('failed');
        });
    void read();
    // The server samples every 15 seconds. We re-read every 30, the same rate
    // as the rest of the tab, and skip it while the browser tab is hidden.
    const timer = window.setInterval(() => {
      if (!document.hidden) void read();
    }, 30_000);
    return () => {
      live = false;
      window.clearInterval(timer);
    };
  }, [window_]);

  // The window picker (1h, 24h, 7d, 30d). Each chart gets its own copy of
  // this row of buttons, but they all share one state, so pressing a button
  // in one row changes every chart (ADR-080). `where` names the row for
  // screen readers.
  const toolbar = (where: string, testPrefix: string) => (
    <p
      className={`${cardStyles.statusRow} op-seg op-seg-wrap`}
      role="group"
      aria-label={`Window for every chart, ${where}`}
    >
      {MACHINE_WINDOWS.map((option) => (
        <button
          key={option}
          type="button"
          className={cardStyles.back}
          aria-pressed={option === window_}
          onClick={() => {
            // Reset to loading here, where the window changes, not in the effect.
            // Clicking the window already shown must do nothing: the effect
            // would not re-run, and the cards would say "Loading" forever.
            if (option === window_) return;
            setWindow(option);
            setMachines(null);
          }}
          data-testid={`${testPrefix}-${option}`}
        >
          {windowName(option)}
        </button>
      ))}
    </p>
  );

  return { machines, latest, window: window_, toolbar };
}
// #endregion machines-read
