import { useEffect, useState } from 'react';
import { INTRO } from '../lib/intro';
import { HEALTH_WORDS, landingHealth, type LandingHealth } from '../lib/landingHealth';
import { proofFigures, type ProofFigure, type TestSummary } from '../lib/landingProof';
import { landingTiles, type LandingTile } from '../lib/siteMap';
import { LINKS, MENUS, type DocKey } from './DocsMenu';
import { NavGlyph } from './SheetIcons';
import styles from './Landing.module.css';

/**
 * The landing page (1.0.1.0): what the address opens with no query. A tile per
 * place the sidebar goes, drawn from the same site map the sidebar is drawn
 * from (src/lib/siteMap.ts), so the two cannot list different things. The
 * large tiles are the map's featured entries, Inventory and Author, each with
 * a photograph in its badge; below them every other section under its group's
 * heading, as the sidebar groups them, then Sign in, Admin and GitHub (1.0.1.4).
 * The Admin tile carries the one live reading, a health dot from /api/health,
 * read once when the page opens, and under the title the evidence strip reads
 * the gate's own counts from /api/tests/summary (1.0.2.0).
 *
 * A section's tile opens what its sidebar section opens first: its first
 * document in the dialog, or its first link in a new tab when it has no
 * documents (API Reference, Diagrams).
 */
/** The strip's four places before the gate's counts arrive: the same boxes, blank, so nothing moves when they fill. */
const PROOF_PLACEHOLDER: ProofFigure[] = ['tests', 'gate', 'records', 'stores'].map((key) => ({
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
}: {
  accountLabel: string;
  onOpenInventory: () => void;
  onOpenAccount: () => void;
  onOpenAdmin: () => void;
  onOpenDoc: (key: DocKey) => void;
}) {
  const { featured, groups, rest } = landingTiles();
  const [health, setHealth] = useState<LandingHealth>('reading');
  const [summary, setSummary] = useState<TestSummary | null>(null);
  useEffect(() => {
    let live = true;
    fetch('/api/health')
      .then((r) => (r.ok ? (r.json() as Promise<{ status?: unknown }>) : null))
      .catch(() => null)
      .then((answer) => {
        if (live) setHealth(landingHealth(answer));
      });
    fetch('/api/tests/summary')
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

  const tile = (entry: LandingTile, large: boolean) => {
    const className = large ? styles.big : styles.tile;
    const badge = large ? `${styles.badge} ${styles.badgeBig}` : styles.badge;
    const name = entry.kind === 'section' ? entry.section.menu : entry.action.key;
    const icon = entry.kind === 'section' ? entry.section.icon : entry.action.icon;
    const title =
      entry.kind === 'section'
        ? MENUS[entry.section.menu].label
        : entry.action.key === 'account'
          ? accountLabel
          : entry.action.label;
    const blurb = entry.kind === 'section' ? entry.section.blurb : entry.action.blurb;
    const photo = entry.kind === 'section' ? entry.section.badgePhoto : entry.action.badgePhoto;
    const isAdmin = entry.kind === 'action' && entry.action.key === 'admin';
    const body = (
      <>
        <span className={photo ? `${badge} ${styles.badgePhoto}` : badge}>
          {photo ? (
            <img
              className={styles.badgeImage}
              src={photo.src}
              srcSet={photo.srcSet}
              sizes={large ? '(max-width: 639px) 64px, 88px' : '64px'}
              width={88}
              height={88}
              alt=""
              title={photo.credit}
              decoding="async"
            />
          ) : (
            <NavGlyph icon={icon} size={large ? 40 : 30} />
          )}
        </span>
        <span className={styles.text}>
          <span className={styles.title}>{title}</span>
          <span className={styles.blurb}>{blurb}</span>
          {isAdmin && (
            <span className={styles.health} data-testid="landing-admin-health" data-state={health}>
              <span className={styles.dot} aria-hidden="true" />
              {HEALTH_WORDS[health].word}
            </span>
          )}
        </span>
      </>
    );
    const testId = `landing-tile-${name}`;
    const tone = isAdmin ? HEALTH_WORDS[health].tone : undefined;

    // A link out: the resume or the repository, or a section with only links.
    const href =
      entry.kind === 'action'
        ? entry.action.key === 'resume' || entry.action.key === 'repo'
          ? LINKS[entry.action.key].href
          : null
        : MENUS[entry.section.menu].items.length === 0
          ? (MENUS[entry.section.menu].links?.[0]?.href ?? null)
          : null;
    if (href) {
      return (
        <li key={name}>
          <a
            className={className}
            href={href}
            target="_blank"
            rel="noreferrer"
            data-testid={testId}
            data-tone={tone}
          >
            {body}
          </a>
        </li>
      );
    }

    const open = () => {
      if (entry.kind === 'section') {
        const first = MENUS[entry.section.menu].items[0];
        if (first) onOpenDoc(first.key);
        return;
      }
      if (entry.action.key === 'inventory') onOpenInventory();
      if (entry.action.key === 'account') onOpenAccount();
      if (entry.action.key === 'admin') onOpenAdmin();
    };
    return (
      <li key={name}>
        <button
          type="button"
          className={className}
          onClick={open}
          data-testid={testId}
          data-tone={tone}
        >
          {body}
        </button>
      </li>
    );
  };

  return (
    <section className={styles.landing} aria-labelledby="landing-title" data-testid="landing">
      <header className={styles.hero}>
        <h1 id="landing-title" className={styles.heading}>
          Welcome to The Yard
        </h1>
        <p className={styles.lede}>{INTRO.sentence}</p>
      </header>
      <ul className={styles.featured} aria-label="Start here">
        {featured.map((entry) => tile(entry, true))}
      </ul>
      {/* The strip stands at its full height from the first paint, four blank
          figures until the gate's counts arrive (1.0.3.7): drawn only once they
          had, it pushed every group below it down 124 px on a desk, the last
          layout shift the landing page had. */}
      <ul
        className={styles.proof}
        aria-label="What this build's gate measured"
        aria-busy={proof === null}
        data-testid="landing-proof"
        data-state={proof === null ? 'loading' : 'ready'}
      >
        {(proof ?? PROOF_PLACEHOLDER).map((figure) => (
          <li
            key={figure.key}
            className={styles.proofItem}
            data-testid={`landing-proof-${figure.key}`}
          >
            <span className={styles.proofFigure}>{figure.figure || '\u00a0'}</span>
            <span className={styles.proofLabel}>{figure.label || '\u00a0'}</span>
            <span className={styles.proofDetail}>{figure.detail || '\u00a0'}</span>
          </li>
        ))}
      </ul>
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
    </section>
  );
}
