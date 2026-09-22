import { INTRO } from '../lib/intro';
import { landingTiles, type LandingTile } from '../lib/siteMap';
import { LINKS, MENUS, type DocKey } from './DocsMenu';
import { NavGlyph } from './SheetIcons';
import styles from './Landing.module.css';

/**
 * The landing page (1.0.1.0): what the address opens with no query. A tile per
 * place the sidebar goes, drawn from the same site map the sidebar is drawn
 * from (src/lib/siteMap.ts), so the two cannot list different things. The
 * large tiles are the map's featured entries, Inventory and Author; the grid
 * is every other section in the sidebar's order, then the other actions.
 *
 * A section's tile opens what its sidebar section opens first: its first
 * document in the dialog, or its first link in a new tab when it has no
 * documents (API Reference, Diagrams).
 */
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
  const { featured, grid } = landingTiles();

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
    const body = (
      <>
        <span className={badge}>
          <NavGlyph icon={icon} size={large ? 40 : 30} />
        </span>
        <span className={styles.text}>
          <span className={styles.title}>{title}</span>
          <span className={styles.blurb}>{blurb}</span>
        </span>
      </>
    );
    const testId = `landing-tile-${name}`;
    const tone = entry.kind === 'action' && entry.action.key === 'admin' ? 'good' : undefined;

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
      <ul className={styles.grid} aria-label="Everything on the site">
        {grid.map((entry) => tile(entry, false))}
      </ul>
    </section>
  );
}
