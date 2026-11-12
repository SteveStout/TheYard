import { useEffect, useState } from 'react';
import { INTRO } from '../../../lib/intro';
import { landingHealth, storesUp, type LandingHealth } from '../../../lib/landingHealth';
import {
  proofFigures,
  ringReading,
  type ProofFigure,
  type TestSummary,
} from '../../../lib/landingProof';
import { landingTiles, type LandingTile as LandingTileEntry } from '../../../lib/siteMap';
import type { DocKey } from '../../../library/documents';
import { MENUS } from '../../../library/sections';
import { CountUp } from '../../shared/CountUp';
import { Hook } from '../../shared/Hook';
import { QualityTrend } from '../../shared/QualityTrend';
import { hookFor, hookTrend } from '../../../lib/pageHooks';
import { Ring } from '../../shared/Ring';
import { PulseBand } from '../PulseBand';
import { TechStack } from '../TechStack';
import { LatestPosts } from '../LatestPosts';
import { LandingBanners } from '../LandingBanners';
import { LandingTile } from '../LandingTile';
import { RECOMMENDATIONS_SOURCE } from '../../../lib/recommendations';
import { Recommendations } from '../../shared/Recommendations';
import styles from './Landing.module.css';
import { request } from '../../../lib/request';

/**
 * The landing page: what the address opens with no query. It opens with its hook (ADR-132): the quality sentence as
 * the page's heading, the Quality page's first chart drawn from the history the build carries, and a link to that
 * page, all drawn with no read, the live readouts directly under it (ADR-100), then who built it in one sentence.
 * Then a tile per
 * place the sidebar goes, drawn from the same site map the sidebar is drawn
 * from (src/lib/siteMap.ts), so the two cannot list different things. The
 * large tiles are the map's featured entries, Inventory and Author with the badge on top, and
 * under them two banners, the resume and the review route; then the evidence strip reads the gate's
 * counts from /api/tests/summary, then the blog's Latest posts, then the Tech stack table the
 * build generated from the repository; What colleagues say in a column of its own on the right, every recommendation in full from
 * /api/recommendations (too narrow for two, it follows). Under both columns, the page's full width, every other section under its group's
 * heading, as the sidebar groups them, then Sign in, Admin and GitHub. The Admin tile's health dot is read from /api/health, the band under the title from /api/pulse.
 * One tile is drawn by LandingTile, the banners by LandingBanners.
 */
/** The landing's hook and its chart, read once from the files the build carries. */
const HOOK = hookFor('landing');
const HOOK_TREND = hookTrend(HOOK);

/** The two figures that are a share of a whole, and so carry a ring (the operator's look). */
const RINGED = ['tests', 'stores'];

/** The strip's three places before the gate's counts arrive: the same boxes, blank, so nothing moves when they fill. */
const PROOF_PLACEHOLDER: ProofFigure[] = ['tests', 'records', 'stores'].map((key) => ({
  key,
  figure: '',
  label: '',
  detail: '',
}));

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
  const { featured, banners, groups, rest } = landingTiles();
  const [health, setHealth] = useState<LandingHealth>('reading');
  const [stores, setStores] = useState<{ up: number; of: number } | null>(null);
  const [summary, setSummary] = useState<TestSummary | null>(null);
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
    request('/api/tests/summary', undefined, { background: true })
      .then((r) => (r.ok ? (r.json() as Promise<TestSummary>) : null))
      .catch(() => null)
      .then((answer) => {
        if (live && answer) setSummary(answer);
      });
    return () => {
      live = false;
    };
  }, []);
  // The record count is the sidebar's own list, so the strip cannot claim records the site does not open.
  const proof = proofFigures(summary, MENUS.records.items.length);

  const opens = { onOpenInventory, onOpenAccount, onOpenAdmin, onOpenDoc };
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
    <section className={styles.landing} aria-labelledby="landing-title" data-testid="landing">
      <Hook
        page="landing"
        lines={HOOK.lines}
        headingId="landing-title"
        headingClass={styles.heading}
        caption={HOOK.chart ? `${HOOK.chart.label}, ${HOOK.chart.direction}` : undefined}
        figure={HOOK_TREND && <QualityTrend data={HOOK_TREND} />}
        link={HOOK.link}
        onLink={(event) => {
          // A plain click opens the page in the app's window; a modified click keeps the browser's own meaning.
          if (event.metaKey || event.ctrlKey || event.shiftKey || event.button !== 0) return;
          event.preventDefault();
          onOpenDoc('quality');
        }}
      />
      <PulseBand onOpenDoc={onOpenDoc} />
      {/* Who built it and what it is, the welcome card's sentence, kept under the readouts the hook sits above. */}
      <p className={styles.who}>{INTRO.sentence}</p>
      <div className={styles.body}>
        <div className={styles.lead}>
          <ul className={styles.featured} aria-label="Start here">
            {featured.map((entry) => tile(entry, true))}
          </ul>
          <LandingBanners banners={banners} onOpenReview={onOpenReview} />
        </div>
        <aside
          className={`${styles.side} op-glass`}
          aria-labelledby="landing-said"
          data-testid="landing-recommendations"
        >
          <h2 id="landing-said" className={styles.groupHeading}>
            <a
              className={styles.headingLink}
              href={RECOMMENDATIONS_SOURCE}
              target="_blank"
              rel="noreferrer"
            >
              What colleagues say
            </a>
          </h2>
          <Recommendations />
        </aside>
        <div className={styles.rest}>
          {/* The strip stands at its full height from the first paint, three blank figures until the
          gate's counts arrive: drawn only once they had, it pushed every group below it down 124 px. */}
          <ul
            className={styles.proof}
            aria-label="What this build's gate measured"
            aria-busy={proof === null}
            data-testid="landing-proof"
            data-state={proof === null ? 'loading' : 'ready'}
          >
            {(proof ?? PROOF_PLACEHOLDER).map((figure) => {
              // The stores are read live from the health answer: how many answered, of how many run.
              const ring =
                figure.key === 'stores' && stores !== null
                  ? {
                      value: stores.up,
                      max: stores.of,
                      words: `${stores.up} of ${stores.of} stores up`,
                    }
                  : figure.ring;
              return (
                <li
                  key={figure.key}
                  className={`${styles.proofItem} op-glass op-tile`}
                  data-testid={`landing-proof-${figure.key}`}
                >
                  <span className={styles.proofFigure}>
                    <CountUp text={figure.figure || '\u00a0'} />
                  </span>
                  <span className={styles.proofLabel}>{figure.label || '\u00a0'}</span>
                  <span className={styles.proofDetail}>{figure.detail || '\u00a0'}</span>
                  {RINGED.includes(figure.key) && (
                    // Drawn from the first paint, an empty track until its reading
                    // arrives, so the figure's row is its full height from the start.
                    <span className={styles.proofRing}>
                      <Ring
                        value={ring?.value ?? 0}
                        max={ring?.max ?? 1}
                        size="tile"
                        inside={ringReading(figure.key, ring)}
                        label={ring?.words ?? null}
                        testId={`landing-proof-ring-${figure.key}`}
                      />
                    </span>
                  )}
                </li>
              );
            })}
          </ul>
          <LatestPosts headingClass={styles.groupHeading} onOpenBlog={onOpenBlog} />
          <TechStack headingClass={styles.groupHeading} />
        </div>
        <div className={styles.wide} data-testid="landing-wide">
          {groups.map((group) => (
            <section
              key={group.key}
              className={styles.group}
              aria-labelledby={`landing-group-${group.key}`}
              data-testid={`landing-group-${group.key}`}
            >
              <h2 id={`landing-group-${group.key}`} className={styles.groupHeading}>
                {group.label}
              </h2>
              <ul className={styles.grid}>{group.tiles.map((entry) => tile(entry, false))}</ul>
            </section>
          ))}
          <ul className={styles.grid} aria-label="Sign in, Admin and the source">
            {rest.map((entry) => tile(entry, false))}
          </ul>
        </div>
      </div>
    </section>
  );
}
