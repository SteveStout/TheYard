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
import type { ActivityReport } from '../lib/activity';
import { adminKey, browserStorage, forgetAdminKey, rememberAdminKey } from '../lib/adminKey';
import {
  clockLabel,
  fromFirstReading,
  keptSparks,
  MACHINE_WINDOWS,
  type MachineWindow,
  timeline,
  trafficTotals,
  windowName,
} from '../lib/machineChart';
import {
  afterColdStart,
  hourTiming,
  sparkCaption,
  sparkRuns,
  type StatTile,
  tilesFrom,
  type TileQuestion,
  visitorsOn,
} from '../lib/statTiles';
import {
  BENCH_QUESTIONS,
  benchCard,
  cardForTile,
  findCards,
  hourGlance,
  neighbours,
  type HourGlance,
} from '../lib/bench';
import type { CardSlug } from '../lib/workbench';
import styles from './AdminPanel.module.css';
import { Readout } from './Readout';
import { Ring } from './Ring';
import type { ErrorEntry, Fetched, Health, Machines, PageStatus } from './admin/types';
import { About, Absent, hourSlots, REFRESH_MS } from './admin/common';

// #region lazy-cards
// One chunk per card (the workbench, measured in the changelog line of the
// version that made it): the Admin chunk is the strip, the rail and the reads
// the strip needs, and a card's code is fetched the first time it is opened.
const HealthCard = lazy(() => import('./admin/HealthCard'));
const AzureCard = lazy(() => import('./admin/AzureCard'));
const PagesCard = lazy(() => import('./admin/PagesCard'));
const TestsCard = lazy(() => import('./admin/TestsCard'));
const TrafficSection = lazy(() => import('./admin/TrafficCard'));
const TelemetryCard = lazy(() => import('./admin/TelemetryCard'));
const TimingCard = lazy(() => import('./admin/TimingCard'));
const BackendsCard = lazy(() => import('./admin/BackendsCard'));
const ProofCard = lazy(() => import('./admin/ProofCard'));
const MachinesCard = lazy(() => import('./admin/MachinesCard'));
const ExperimentCard = lazy(() => import('./admin/ExperimentCard'));
const SqlCard = lazy(() => import('./admin/SqlCard'));
const StoreCard = lazy(() => import('./admin/StoreCard'));
const ErrorsCard = lazy(() => import('./admin/ErrorsCard'));
const LogCard = lazy(() => import('./admin/LogCard'));
const KeptLogsCard = lazy(() => import('./admin/KeptLogsCard'));
const ActivityCard = lazy(() => import('./admin/ActivityCard'));
const OperatorCard = lazy(() => import('./admin/OperatorCard'));
const ResetLinkCard = lazy(() => import('./admin/ResetLinkCard'));
// #endregion lazy-cards

/**
 * The Admin tab (ADR-010): the running system reporting on itself. Three
 * cards fetch independently and degrade independently, so a dead Azure
 * leg never hides app health. Public on purpose; the ADR explains why.
 */
/**
 * The operator's key, read from the address bar once, when the module loads,
 * and otherwise from what this browser remembered (src/lib/adminKey.ts).
 * Once and not per render, because the app mirrors its own view into the
 * address bar and drops anything it did not put there, which takes the key
 * out of the URL on the first render; that is welcome, since a key in an
 * address bar outlives the tab in the history, and it means the key has to be
 * read before that mirror runs (the 1.0.0.114 gate, take one). Remembered,
 * because the file the key lives in is on one machine and the operator
 * reads the site from his phone (the 1.0.0.120 change).
 */
const ADMIN_KEY = adminKey();

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
  /** Where the back button goes, as its label says (1.0.3.9). */
  backTo?: 'home' | 'inventory';
  signedIn: boolean;
  onOpenAccount: () => void;
  /** The card the address names, and the one pinned beside it (the workbench). */
  card?: CardSlug;
  pin?: CardSlug | null;
  /** A name the address gave that is no card, said in the rail; null when there was none. */
  cardAsked?: string | null;
  onOpenCard?: (slug: CardSlug) => void;
  onPin?: (slug: CardSlug | null) => void;
}) {
  // The operator's key: state, so forgetting it takes effect on the cards
  // at once; its first value is the one read when the module loaded.
  const [adminKey, setAdminKey] = useState<string | null>(ADMIN_KEY);
  const forgetKey = () => {
    forgetAdminKey(browserStorage());
    setAdminKey(null);
  };
  const enterKey = (entered: string) => {
    const key = rememberAdminKey(entered, browserStorage());
    if (key !== null) setAdminKey(key);
  };
  // Whether this site serves the per-visitor rows, learned from the activity
  // report; null until it has answered. Off by default (13 September).
  const [rowsServed, setRowsServed] = useState<boolean | null>(null);
  // What the strip of tiles needs from the cards that do their own reading:
  // today's people from the activity report, and the last page sweep.
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
    // Not while the tab is hidden: a page nobody is looking at reads nothing
    // (pipelane, 2026-09-21). The strip and the open card follow this clock.
    const id = window.setInterval(() => {
      if (!document.hidden) setTick((t) => t + 1);
    }, REFRESH_MS);
    return () => window.clearInterval(id);
  }, []);

  useEffect(() => {
    let live = true;
    // The strip's own reads (the workbench): health, the error ring and the page
    // sweep. Every card reads for itself, and only while it is open.
    // A failed or non-200 answer marks the card failed instead of leaving it loading forever.
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
    // The strip keeps its own read of the page sweep (the workbench): the pages
    // card that shows it in full is not on the page unless it is the open card.
    void grab<PageStatus>('/api/admin/pages', (answer) => {
      if (answer !== null && answer !== 'failed' && answer.report !== null) {
        setPagesSeen({ checked: answer.report.checked, up: answer.report.up });
      }
    });
    return () => {
      live = false;
    };
  }, [tick]);

  // Today's people, for the strip and for whether this site serves its kept
  // rows, read once when the tab opens; the activity card reads its own window
  // when it is open, and says what it read back through onActivity.
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
  // The strip is made of what the cards below have already read, by the rules
  // in statTiles.ts; it asks the server for nothing of its own, so a tile and
  // the card it points at cannot disagree.
  const seen = latestMachines;
  const hour = seen === null ? null : hourSlots(seen);
  // The lines under the tiles follow the window every chart follows, once the
  // answer for that window has arrived and the store keeps it.
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
  // The minutes of a cold start are not held against the hour (statTiles.ts,
  // the cold-start region). When the process started is its newest sample's
  // time less its uptime, both from the one answer, so nothing here reads a clock.
  const startedAt =
    seen === null || lastSample === null
      ? null
      : new Date(new Date(lastSample.at).getTime() - seen.container.uptime_seconds * 1000);
  const warmed = hour === null ? null : afterColdStart(hour, startedAt);
  // Every request and every error in the hour is still counted; the median
  // and the ninety-fifth are the warm minutes' own, over every request in
  // them (statTiles.ts, hourTiming).
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
  // Every card is a chunk of its own, fetched when the workbench opens it or
  // pins it, and each reads only while it is on the page (ADR: The Admin tab,
  // as a product, the addendum on the workbench). What the strip reads above is
  // handed to the cards that show it in full, rather than read twice.
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
        <button type="button" className={styles.back} onClick={onBack}>
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
 * A card while its chunk is on the way: its name, at the height the page
 * reserves for a card, so nothing under it moves when it lands (1.0.3.7's rule).
 */
function CardLoading({ slug }: { slug: CardSlug }) {
  return (
    <article
      className={`${styles.wide} ${styles.cardLoading} op-glass`}
      data-testid="bench-loading"
    >
      <h2 className={styles.cardTitle}>{benchCard(slug).name}</h2>
      <p className={styles.muted} role="status">
        Loading…
      </p>
    </article>
  );
}

// #region workbench
/**
 * A click on a link the workbench handles itself: a plain click opens the card
 * in place, and a click that asks for a new tab or window (a modifier key, the
 * middle button) is left to the browser, which follows the address.
 */
function follow(event: MouseEvent<HTMLElement>, open: () => void) {
  if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
    return;
  }
  event.preventDefault();
  open();
}

/**
 * The workbench (ADR: The Admin tab, as a product, the addendum on the
 * workbench): the rail of the five questions down the left, one card large
 * beside it, and a second column for the card that is pinned. The keys j and
 * k walk the rail's order wherever focus is, except in a field being typed in.
 */
/** Whether a media query matches now, and again whenever that changes. */
function useMatches(query: string): boolean {
  const [matches, setMatches] = useState(() => window.matchMedia(query).matches);
  useEffect(() => {
    const list = window.matchMedia(query);
    const change = () => setMatches(list.matches);
    list.addEventListener('change', change);
    return () => list.removeEventListener('change', change);
  }, [query]);
  return matches;
}

/**
 * The rail's contents: the search box, the note for a name that is no card, and
 * the five questions with their cards. Beside the card on a desk; inside the
 * Cards drawer on a phone.
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
 * The workbench (ADR: The Admin tab, as a product, the addendum on the
 * workbench): the rail of the five questions down the left, one card large
 * beside it, and a second column for the card that is pinned. The keys j and
 * k walk the rail's order wherever focus is, except in a field being typed in.
 * Under 600 px the rail is a drawer behind a Cards button and the pinned card
 * is a fold under the open one, which reads nothing until it is unfolded.
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
  const phone = useMatches('(max-width: 599px)');
  const wide = useMatches('(min-width: 1440px)');
  const drawerRef = useRef<HTMLDialogElement>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [foldOpen, setFoldOpen] = useState(false);
  const { previous, next } = neighbours(open);
  const openCard = benchCard(open);
  const question = BENCH_QUESTIONS.find((entry) => entry.key === openCard.question);
  const pinShown = pin !== null && pin !== open;
  // The hour at a glance is on the Admin home and the two cards it summarises,
  // timing and traffic, and nowhere else (the tweaks pass, A7). It stands beside
  // the card on a wide desk with nothing pinned, and above it everywhere else,
  // where a third column would squeeze it.
  const hourHere = HOUR_CARDS.includes(open);
  const hourBeside = hourHere && wide && !phone && !pinShown;

  // A layout effect, so the keys are listened for in the same commit that draws the card: a j pressed the
  // moment the card is on the page walks the rail rather than falling between the paint and a passive effect.
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
  // The drawer is a native dialog, the shape the site's own drawer has (SideNav):
  // the button flips the flag, and this mirrors it onto the dialog.
  useEffect(() => {
    const drawer = drawerRef.current;
    if (!drawer) return;
    if (drawerOpen && !drawer.open) drawer.showModal();
    if (!drawerOpen && drawer.open) drawer.close();
  }, [drawerOpen, phone]);
  const openFromDrawer = (slug: CardSlug) => {
    setDrawerOpen(false);
    onOpen(slug);
  };
  // #endregion bench-drawer

  return (
    <div className={styles.bench} data-testid="workbench">
      {phone ? (
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
                  className={styles.back}
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
        {phone && asked !== null && (
          <p className={styles.railNote} role="status" data-testid="bench-unknown">
            No card is called &lsquo;{asked}&rsquo;, so this is {openCard.name}.
          </p>
        )}
        <div className={styles.benchBar}>
          {phone && (
            <button
              type="button"
              className={styles.back}
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
            {/* Pin sits in the card's header row beside Previous and Next, where
                the card's controls live (the tweaks pass, A7). */}
            <button
              type="button"
              className={`${styles.back} ${styles.pinButton}`}
              aria-pressed={pin === open}
              onClick={() => onPin(pin === open ? null : open)}
              data-testid="bench-pin"
            >
              <span className={styles.pinDot} aria-hidden="true" />
              {pin === open ? 'Pinned' : 'Pin'}
            </button>
            <p
              className={`${styles.stepper} op-seg`}
              role="group"
              aria-label="The card before and after"
            >
              <button
                type="button"
                className={styles.back}
                onClick={() => onOpen(previous)}
                aria-label={`Previous card, ${benchCard(previous).name}`}
                data-testid="bench-previous"
              >
                Previous
              </button>
              <button
                type="button"
                className={styles.back}
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
          data-hour={hourBeside ? 'beside' : 'above'}
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
                  className={styles.back}
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
                      className={styles.back}
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

/** The cards the hour at a glance stands with: the Admin home and the two it summarises (A7). */
const HOUR_CARDS: readonly CardSlug[] = ['health', 'timing', 'traffic'];

/**
 * The hour at a glance (the operator's look): the typical answer inside the
 * ninety-fifth as two rings against ten milliseconds, the number in words in
 * the middle, and the hour's counts as a readout under it. Made of what the
 * strip read, so it and the tiles say the same thing.
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
 * The strip across the top (ADR: The Admin tab, as a product): eight tiles
 * under four questions, each a button that goes to the cards that answer it.
 * What a tile says and what colour it is are decided in statTiles.ts, which
 * has no React in it; this only draws. The tone is a word as well as a
 * colour, because a colour alone says nothing to somebody who cannot see it.
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
  /** A tile is a link to the card that answers it (the workbench). */
  onOpenCard: (slug: CardSlug) => void;
  toolbar: ReactNode;
  caption: string;
}) {
  const toneClass: Record<StatTile['tone'], string> = {
    good: styles.tileGood,
    warn: styles.tileWarn,
    bad: styles.tileBad,
    plain: styles.tilePlain,
    waiting: styles.tileWaiting,
  };
  const ordered = QUESTION_ORDER.flatMap((question) =>
    tiles.filter((tile) => tile.question === question)
  );
  return (
    <>
      <div className={styles.stripHead}>
        {toolbar}
        <p className={styles.muted} data-testid="strip-caption">
          {caption}
        </p>
      </div>
      <ul
        className={`${styles.strip} ${styles.statStrip}`}
        aria-label="The site at a glance"
        data-testid="stat-strip"
      >
        {ordered.map((tile) => {
          const runs = tile.spark === undefined ? [] : sparkRuns(tile.spark, 100, 24);
          const word = TONE_WORD[tile.tone];
          return (
            <li key={tile.key} className={styles.stripItem}>
              <a
                href={`?view=admin&card=${cardForTile(tile.key)}`}
                className={`${styles.tile} op-glass op-tile ${toneClass[tile.tone]}`}
                data-testid={`tile-${tile.key}`}
                data-tone={tile.tone}
                onClick={(event) => follow(event, () => onOpenCard(cardForTile(tile.key)))}
              >
                {/* Name, value row, foot (the tweaks pass, A2): the rail names the section,
                    so the tile no longer repeats its question; the value row holds the ring
                    when there is one, and every tile in a row sets its number on one baseline. */}
                <span className={styles.tileLabel}>{tile.label}</span>
                <span className={styles.tileValueRow}>
                  <span className={styles.tileValue}>{tile.value}</span>
                  {/* The ring beside a tile's number (ADR: The glass look): a share of a known
                      whole, hidden from a screen reader because the tile says the number in
                      words. Its box is on every tile from the first paint, drawn or not, so
                      the number beside it wraps the same before the reading arrives as after. */}
                  <span className={styles.tileRing}>
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
                </span>
                <span className={styles.tileDetail} title={tile.detail}>
                  {tile.detail}
                </span>
                {/* A tile keeps the room its word and its line will take, so nothing under the
                    strip moves when the hour's reading arrives. */}
                {word !== null ? (
                  <span className={styles.tileTone}>{word}</span>
                ) : (
                  <span className={styles.tileToneSlot} aria-hidden="true" />
                )}
                {runs.length === 0 && <span className={styles.sparkSlot} aria-hidden="true" />}
                {runs.length > 0 && (
                  <svg
                    className={styles.spark}
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
 * One read for the traffic card, the machines card and the tiles over the
 * page, and one window for all three: they are only worth looking at side by
 * side over the same stretch. A hook and not a component since 1.0.0.161,
 * because the two cards now sit under different questions.
 */
function useMachines(): {
  machines: Fetched<Machines>;
  latest: Machines | null;
  window: MachineWindow;
  toolbar: (where: string, testPrefix: string) => ReactNode;
} {
  const [machines, setMachines] = useState<Fetched<Machines>>(null);
  // The last answer that arrived, which a change of window does not take
  // away: the tiles are made of it, and eight tiles going back to "waiting"
  // because somebody asked a chart for a week would be the page flinching.
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
    // The sampler takes a reading every fifteen seconds; the card follows at
    // half a minute, which is the rate the rest of this tab refreshes at.
    const timer = window.setInterval(() => {
      if (!document.hidden) void read();
    }, 30_000);
    return () => {
      live = false;
      window.clearInterval(timer);
    };
  }, [window_]);

  // One window, and a row of buttons wherever a chart is: over the tiles, on
  // the traffic card and on the machines card, which sit under different
  // questions and a long scroll apart. Every row is the same state, so
  // pressing one presses all three (ADR: The Admin tab, as a product, the
  // addendum on one window for every chart).
  const toolbar = (where: string, testPrefix: string) => (
    <p
      className={`${styles.statusRow} op-seg op-seg-wrap`}
      role="group"
      aria-label={`Window for every chart, ${where}`}
    >
      {MACHINE_WINDOWS.map((option) => (
        <button
          key={option}
          type="button"
          className={styles.back}
          aria-pressed={option === window_}
          onClick={() => {
            // The change of window is the event, and the cards go back to
            // loading here rather than inside the effect, as the activity
            // card's do and for the reason it gives.
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
