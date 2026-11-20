import { useEffect, useState, useSyncExternalStore, type MouseEvent } from 'react';
import { useReviewReadings } from '../../../hooks/useReviewReadings';
import { INTRO } from '../../../lib/intro';
import { landingHealth, storesUp, type LandingHealth } from '../../../lib/landingHealth';
import {
  DOCK_REVIEW,
  LANDING_CLOSE,
  MOVES,
  SECTION_NUMBERS,
  START_HERE,
} from '../../../lib/landingMoves';
import { landingTiles, type LandingTile as LandingTileEntry } from '../../../lib/siteMap';
import type { DocKey } from '../../../library/documents';
import { LINKS, MENUS } from '../../../library/sections';
import { useFirstSeen } from '../../../hooks/useFirstSeen';
import { Hook } from '../../shared/Hook';
import { SectionHead } from '../../shared/SectionHead';
import { WhereNext } from '../../shared/WhereNext';
import { QualityTrend } from '../../shared/QualityTrend';
import { hookFor, hookTrend, landingLines, stoppedLine } from '../../../lib/pageHooks';
import { centralDay, releasesInLastSevenDays } from '../../../lib/releaseFacts';
import RELEASES from 'virtual:release-facts';
import { ProofStrip } from '../ProofStrip';
import { PulseBand } from '../PulseBand';
import { TechStackSummary } from '../TechStack';
import { LatestPosts } from '../LatestPosts';
import { LandingVoices } from '../LandingVoices';
import { LandingTile } from '../LandingTile';
import { NewestPost } from '../NewestPost';
import { REVIEW_ADDRESS } from '../../../lib/reviewStops';
import { BOOK_ADDRESS, VERDICT } from '../../../lib/reviewVerdict';
import { BookSheet } from '../../review/BookSheet';
import { PdfMark } from '../../shared/PdfMark';
import { ReviewClose } from '../../review/ReviewClose';
import { ReviewDock } from '../../review/ReviewDock';
import styles from './Landing.module.css';
import { request } from '../../../lib/request';

/**
 * The landing page: what the address opens with no query (ADR-082, the addendum on the home page's order). It opens
 * with its hook in the split layout (ADR-132): on the left the name line, the quality sentence as the page's heading,
 * who built it in one sentence, the hook's sentences and four moves (Check my work, the resume, Book 20 minutes, the
 * live auction);
 * on the right the Quality page's first chart drawn from the history the build carries, its link and the newest
 * post. Then the live readings (ADR-100); Start here, four doors in one row with what each costs; the blog's Latest
 * posts beside three named voices and the way to all of them; the build, the gate's three figures and the stack's
 * summary with the way to the whole table on its own page; the small tiles under their groups, drawn from the same
 * site map the sidebar is drawn from (src/lib/siteMap.ts), Sign in and Admin left to the sidebar; the close; and
 * Where next. On a phone a dock keeps the next step in reach once the moves have scrolled away. Every section has
 * the page's one width and one gap. It takes the sales pages' look (ADR: The sales pages set the site's look): the
 * live readings are the dark live panel on its side, every group opens with the shared section head and its number,
 * 01 to 07, and the close asks the review route's question under 08; the primary move's arrow nudges right when the
 * moves are first seen.
 */
/** The day never changes under a mounted page in a way worth a redraw, so nothing is subscribed. */
const noSubscription = () => () => {};
const visitorsDay = () => centralDay(new Date());
const buildsDay = () => RELEASES.builtOn;

const HOOK = hookFor('landing');
const HOOK_TREND = hookTrend(HOOK);

/** A plain click, which the page answers itself; any other keeps the browser's own meaning. */
const plainClick = (event: MouseEvent) =>
  !(event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0);

/** Opens a view in place on a plain click; the address stays a real one for a crawler and a new tab. */
const inPlace = (open: () => void) => (event: MouseEvent<HTMLAnchorElement>) => {
  if (!plainClick(event)) return;
  event.preventDefault();
  open();
};

export function Landing({
  accountLabel,
  onOpenInventory,
  onOpenAccount,
  onOpenAdmin,
  onOpenDoc,
  onOpenBlog,
  onOpenReview,
}: {
  accountLabel: string;
  onOpenInventory: () => void;
  onOpenAccount: () => void;
  onOpenAdmin: () => void;
  onOpenDoc: (key: DocKey) => void;
  onOpenBlog: (slug: string | null) => void;
  onOpenReview: () => void;
}) {
  const { doors, explore } = landingTiles();
  const [health, setHealth] = useState<LandingHealth>('reading');
  const [stores, setStores] = useState<{ up: number; of: number } | null>(null);
  const [booking, setBooking] = useState(false);
  const { summary, recommendations } = useReviewReadings();
  // The week's releases, counted from the day the build ran while the page drawn ahead of a visit is taken over, so
  // the two draws agree, then from the visitor's own Central day (ADR-132).
  const today = useSyncExternalStore(noSubscription, visitorsDay, buildsDay);
  const releases = releasesInLastSevenDays(RELEASES.days, today);
  const lines = landingLines(HOOK, releases);
  // The holder of the primary move's nudge.
  const [moves] = useFirstSeen<HTMLDivElement>();
  useEffect(() => {
    let live = true;
    request('/api/health', undefined, { background: true })
      .then((r) => (r.ok ? (r.json() as Promise<{ status?: unknown; checks?: unknown }>) : null))
      .catch(() => null)
      .then((answer) => {
        if (!live) return;
        setHealth(landingHealth(answer));
        setStores(storesUp(answer));
      });
    return () => {
      live = false;
    };
  }, []);

  const opens = { onOpenInventory, onOpenAccount, onOpenAdmin, onOpenDoc, onOpenReview };
  const tile = (entry: LandingTileEntry, large: boolean) => (
    <LandingTile
      key={entry.kind === 'section' ? entry.section.menu : entry.action.key}
      entry={entry}
      large={large}
      accountLabel={accountLabel}
      health={health}
      open={opens}
    />
  );

  return (
    <>
      <section className={styles.landing} aria-labelledby="landing-title" data-testid="landing">
        <Hook
          page="landing"
          lines={lines}
          headingId="landing-title"
          headingClass={styles.heading}
          caption={HOOK.chart ? `${HOOK.chart.label}, ${HOOK.chart.direction}` : undefined}
          figure={HOOK_TREND && <QualityTrend data={HOOK_TREND} />}
          link={HOOK.link}
          onLink={inPlace(() => onOpenDoc('quality'))}
          split={{
            above: (
              <p className={styles.eyebrow} data-testid="landing-name">
                {VERDICT.eyebrow.map((part, index) => (
                  <span key={part}>
                    {index > 0 && <i aria-hidden="true">/</i>}
                    {part}
                  </span>
                ))}
              </p>
            ),
            lede: INTRO.sentence,
            moves: (
              <div
                ref={moves}
                className={styles.moves}
                data-mock="home-moves"
                data-testid="landing-moves"
              >
                <a
                  className={`op-pill-primary ${styles.primaryMove}`}
                  href={REVIEW_ADDRESS}
                  onClick={inPlace(onOpenReview)}
                  data-testid="landing-primary"
                >
                  {MOVES.primary}
                  <span className="op-nudge-x" aria-hidden="true">
                    →
                  </span>
                </a>
                <a
                  className="op-pill-resume"
                  href={LINKS.resume.href}
                  target="_blank"
                  rel="noreferrer"
                  data-testid="landing-resume"
                >
                  <PdfMark />
                  {MOVES.resume}
                </a>
                <a
                  className={`op-pill-secondary ${styles.move}`}
                  href={BOOK_ADDRESS}
                  onClick={() => setBooking(true)}
                  data-action="landing.book"
                  data-testid="landing-book"
                >
                  {MOVES.book}
                </a>
                <a
                  className={styles.textLink}
                  href="/?view=inventory"
                  onClick={inPlace(onOpenInventory)}
                >
                  {MOVES.inventory}
                </a>
              </div>
            ),
            after: <NewestPost onOpenBlog={onOpenBlog} />,
          }}
        />
        <PulseBand onOpenDoc={onOpenDoc} />
        <section
          className={styles.section}
          aria-labelledby="landing-start"
          data-mock="start"
          data-testid="landing-start"
        >
          <SectionHead id="landing-start" number={SECTION_NUMBERS.start} title={START_HERE} seen />
          <ul className={styles.doors}>{doors.map((entry) => tile(entry, true))}</ul>
        </section>
        <section className={styles.pv} aria-label="Posts and voices">
          <LatestPosts onOpenBlog={onOpenBlog} />
          <LandingVoices recommendations={recommendations} />
        </section>
        <section
          className={`${styles.section} ${styles.build}`}
          aria-labelledby="landing-stack"
          data-mock="stack"
          data-testid="landing-build"
        >
          <SectionHead
            id="landing-stack"
            number={SECTION_NUMBERS.stack}
            title={MENUS.stack.label}
            seen
          />
          <div className={styles.buildRow}>
            <ProofStrip summary={summary} stores={stores} />
            <TechStackSummary />
          </div>
          {/* The last release the gate turned back (G5), under the stack's header. Left out when none is kept. */}
          {RELEASES.stopped && (
            <p className={styles.stopped} data-testid="landing-stopped">
              {stoppedLine(RELEASES.stopped)}
            </p>
          )}
        </section>
        <div className={styles.explore} data-testid="landing-wide">
          {explore.map((group) => (
            <section
              key={group.key}
              className={styles.group}
              aria-labelledby={`landing-group-${group.key}`}
              data-mock={group.key === 'who' ? 'who' : undefined}
              data-testid={`landing-group-${group.key}`}
            >
              <SectionHead
                id={`landing-group-${group.key}`}
                number={SECTION_NUMBERS[group.key]}
                title={group.label}
                seen
              />
              <ul className={styles.small}>{group.tiles.map((entry) => tile(entry, false))}</ul>
            </section>
          ))}
        </div>
        <ReviewClose
          onAction={() => {}}
          onBook={() => setBooking(true)}
          ask={LANDING_CLOSE}
          heading={LANDING_CLOSE.label}
        />
        <WhereNext page="landing" />
      </section>
      <ReviewDock
        watch='[data-testid="landing-primary"]'
        hidden={booking}
        moves={[
          { label: DOCK_REVIEW, href: REVIEW_ADDRESS, onClick: inPlace(onOpenReview) },
          { label: MOVES.resume, href: LINKS.resume.href, onClick: () => {}, resume: true },
        ]}
      />
      <BookSheet open={booking} onClose={() => setBooking(false)} onAction={() => {}} />
    </>
  );
}
