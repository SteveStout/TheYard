// Draws one Admin card by its name, for the open column or the pinned one. Each card's code is
// a chunk of its own, loaded the first time the card is shown, with a placeholder of the same
// height while it arrives. Data the strip already read is handed in rather than read again.
// It is its own file because choosing and loading a card is one job, apart from the layout.
import { lazy, type ReactNode, Suspense } from 'react';
import type { ActivityReport } from '../../../lib/activity';
import { benchCard } from '../../../lib/bench';
import type { MachineWindow } from '../../../lib/machineChart';
import type { CardSlug } from '../../../lib/workbench';
import styles from '../AdminPanel/AdminPanel.module.css';
import cardStyles from '../shared/card.module.css';
import type { ErrorEntry, Fetched, Health, Machines } from '../shared/types';
import { Absent } from '../shared/common';

// #region lazy-cards
/**
 * Each card is its own code chunk. `lazy` means the card's code is not in the
 * Admin tab's own chunk, so the tab itself stays small: just the strip, the
 * rail and the strip's reads. Once the tab has mounted and the browser is
 * idle, every card's chunk is fetched ahead of the click (src/lib/prefetch.ts),
 * so choosing a card waits on its data and not on its code.
 */
export const CARD_CHUNKS = {
  HealthCard: () => import('../HealthCard/HealthCard'),
  AzureCard: () => import('../AzureCard/AzureCard'),
  PagesCard: () => import('../PagesCard/PagesCard'),
  TestsCard: () => import('../TestsCard/TestsCard'),
  TrafficSection: () => import('../TrafficCard/TrafficCard'),
  TelemetryCard: () => import('../TelemetryCard/TelemetryCard'),
  TimingCard: () => import('../TimingCard/TimingCard'),
  BackendsCard: () => import('../BackendsCard/BackendsCard'),
  ProofCard: () => import('../ProofCard/ProofCard'),
  SpendCard: () => import('../SpendCard/SpendCard'),
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
/** The Health card, its code fetched the first time it is drawn. */
const HealthCard = lazy(CARD_CHUNKS.HealthCard);
/** The Azure card, its code fetched the first time it is drawn. */
const AzureCard = lazy(CARD_CHUNKS.AzureCard);
/** The Pages card, its code fetched the first time it is drawn. */
const PagesCard = lazy(CARD_CHUNKS.PagesCard);
/** The Tests card, its code fetched the first time it is drawn. */
const TestsCard = lazy(CARD_CHUNKS.TestsCard);
/** The Traffic card, its code fetched the first time it is drawn. */
const TrafficSection = lazy(CARD_CHUNKS.TrafficSection);
/** The Telemetry card, its code fetched the first time it is drawn. */
const TelemetryCard = lazy(CARD_CHUNKS.TelemetryCard);
/** The Timing card, its code fetched the first time it is drawn. */
const TimingCard = lazy(CARD_CHUNKS.TimingCard);
/** The Backends card, its code fetched the first time it is drawn. */
const BackendsCard = lazy(CARD_CHUNKS.BackendsCard);
/** The Proof card, its code fetched the first time it is drawn. */
const ProofCard = lazy(CARD_CHUNKS.ProofCard);
/** The cost card, its code fetched the first time it is drawn. */
const SpendCard = lazy(CARD_CHUNKS.SpendCard);
/** The Machines card, its code fetched the first time it is drawn. */
const MachinesCard = lazy(CARD_CHUNKS.MachinesCard);
/** The Experiment card, its code fetched the first time it is drawn. */
const ExperimentCard = lazy(CARD_CHUNKS.ExperimentCard);
/** The SQL card, its code fetched the first time it is drawn. */
const SqlCard = lazy(CARD_CHUNKS.SqlCard);
/** The Store card, its code fetched the first time it is drawn. */
const StoreCard = lazy(CARD_CHUNKS.StoreCard);
/** The Errors card, its code fetched the first time it is drawn. */
const ErrorsCard = lazy(CARD_CHUNKS.ErrorsCard);
/** The Log card, its code fetched the first time it is drawn. */
const LogCard = lazy(CARD_CHUNKS.LogCard);
/** The kept log card, its code fetched the first time it is drawn. */
const KeptLogsCard = lazy(CARD_CHUNKS.KeptLogsCard);
/** The Activity card, its code fetched the first time it is drawn. */
const ActivityCard = lazy(CARD_CHUNKS.ActivityCard);
/** The Operator card, its code fetched the first time it is drawn. */
const OperatorCard = lazy(CARD_CHUNKS.OperatorCard);
/** The reset link card, its code fetched the first time it is drawn. */
const ResetLinkCard = lazy(CARD_CHUNKS.ResetLinkCard);
// #endregion lazy-cards

/** What a card may need from the page: the shared reads, the chart window and the operator's key. */
export type AdminBenchCardProps = {
  /** Which card to draw. */
  slug: CardSlug;
  /** The page's refresh tick; a card re-reads its data when it moves. */
  tick: number;
  health: Fetched<Health>;
  errors: Fetched<ErrorEntry[]>;
  machines: Fetched<Machines>;
  /** The chart window every chart on the tab follows. */
  window: MachineWindow;
  /** Draws a row of window buttons for a card's own copy of the picker. */
  toolbar: (where: string, testPrefix: string) => ReactNode;
  signedIn: boolean;
  onOpenAccount: () => void;
  /** Whether this site serves per-visitor rows; null until the activity report answers. */
  rowsServed: boolean | null;
  /** The operator's key, or null when there is none. */
  adminKey: string | null;
  /** Hands an activity report the Activity card read back to the strip. */
  onActivity: (report: ActivityReport) => void;
  /** Hands a page check the Pages card read back to the strip. */
  onPagesReport: (seen: { checked: number; up: number }) => void;
  onEnterKey: (entered: string) => void;
  onForgetKey: () => void;
};

// #region bench-cards
/**
 * Draws one card by its slug (ADR-080). A card's code loads the first time
 * it is opened or pinned, and it reads data only while on the page. Data the
 * strip already read (health, errors, machines) is passed in, not re-read.
 * Suspense shows CardLoading while the card's code downloads.
 */
export function AdminBenchCard(props: AdminBenchCardProps) {
  const { slug, tick, machines, window: machineWindow, toolbar, adminKey, rowsServed } = props;
  const drawn = (() => {
    switch (slug) {
      case 'health':
        return <HealthCard health={props.health} />;
      case 'azure':
        return <AzureCard tick={tick} />;
      case 'pages':
        return <PagesCard onReport={props.onPagesReport} />;
      case 'tests':
        return <TestsCard />;
      case 'traffic':
        return (
          <TrafficSection
            machines={machines}
            window={machineWindow}
            toolbar={toolbar('on the traffic card', 'machines-window')}
          />
        );
      case 'telemetry':
        return <TelemetryCard tick={tick} />;
      case 'timing':
        return <TimingCard tick={tick} />;
      case 'backends':
        return <BackendsCard tick={tick} />;
      case 'proof':
        return (
          <ProofCard tick={tick} signedIn={props.signedIn} onOpenAccount={props.onOpenAccount} />
        );
      case 'spend':
        return (
          <SpendCard
            tick={tick}
            window={machineWindow}
            toolbar={toolbar('on the cost card', 'spend-window')}
          />
        );
      case 'machines':
        return (
          <MachinesCard
            machines={machines}
            window={machineWindow}
            toolbar={toolbar('on the machines card', 'machines-card-window')}
          />
        );
      case 'experiment':
        return <ExperimentCard tick={tick} />;
      case 'sql':
        return <SqlCard tick={tick} />;
      case 'store':
        return <StoreCard tick={tick} />;
      case 'errors':
        return <ErrorsCard errors={props.errors} tick={tick} />;
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
        return (
          <ActivityCard adminKey={adminKey} rowsServed={rowsServed} onReport={props.onActivity} />
        );
      case 'operator':
        return (
          <OperatorCard
            adminKey={adminKey}
            onEnterKey={props.onEnterKey}
            onForget={props.onForgetKey}
          />
        );
      case 'reset':
        return <ResetLinkCard adminKey={adminKey} />;
    }
  })();
  return <Suspense fallback={<CardLoading slug={slug} />}>{drawn}</Suspense>;
}
// #endregion bench-cards

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
