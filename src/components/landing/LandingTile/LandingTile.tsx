/**
 * Does:      Draws one landing tile from the site map, large (a door under Start here, with the chip that says what it
 *            costs) or small: its badge (a photograph or the map's icon), its title and one line, the Admin tile's
 *            health dot, as a link out, a link that opens the review route in place, or a button that opens a view.
 * Does not:  Decide which tiles exist or their order (siteMapLanding.ts does) or read the health (Landing.tsx does).
 * Used by:   Landing.tsx.
 */
import type { MouseEvent } from 'react';
import { HEALTH_WORDS, type LandingHealth } from '../../../lib/landingHealth';
import { REVIEW_ADDRESS } from '../../../lib/reviewStops';
import type { LandingTile as Tile } from '../../../lib/siteMap';
import type { DocKey } from '../../../library/documents';
import { LINKS, MENUS } from '../../../library/sections';
import { NavGlyph } from '../../shared/SheetIcons';
import styles from '../Landing/Landing.module.css';

/** What a tile opens that is a view of the app rather than a link: the inventory, the account page, Admin, a document. */
export type TileOpeners = {
  onOpenInventory: () => void;
  onOpenAccount: () => void;
  onOpenAdmin: () => void;
  onOpenDoc: (key: DocKey) => void;
  onOpenReview: () => void;
};

/**
 * A section's tile opens what its sidebar section opens first: its first document in the dialog,
 * or its first link in a new tab when it has none (API Reference, Diagrams).
 */
export function LandingTile({
  entry,
  large,
  accountLabel,
  health,
  open,
}: {
  entry: Tile;
  large: boolean;
  accountLabel: string;
  health: LandingHealth;
  open: TileOpeners;
}) {
  // The two doors that were banners keep the banner's own title size, as the home page's picture draws them.
  const banner = large && entry.kind === 'action' && entry.action.banner === true;
  const className = large
    ? `${styles.big} ${banner ? styles.banner : ''} op-glass`
    : `${styles.tile} op-glass`;
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
  const cost = large
    ? entry.kind === 'section'
      ? entry.section.cost
      : entry.action.cost
    : undefined;
  const isAdmin = entry.kind === 'action' && entry.action.key === 'admin';
  const body = (
    <>
      <span className={photo ? `${badge} ${styles.badgePhoto}` : badge}>
        {photo ? (
          <img
            className={styles.badgeImage}
            src={photo.src}
            srcSet={photo.srcSet}
            sizes={large ? '(max-width: 639.98px) 64px, 88px' : '64px'}
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
      {cost && <span className={styles.chip}>{cost}</span>}
    </>
  );
  // The two doors that were banners keep their names, so a reader of the tests finds them where they were.
  const testId =
    large && entry.kind === 'action' && (name === 'review' || name === 'resume')
      ? `landing-banner-${name}`
      : `landing-tile-${name}`;
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
  // The review route: a real address, opened in place on a plain click.
  if (entry.kind === 'action' && entry.action.key === 'review') {
    const onReview = (event: MouseEvent<HTMLAnchorElement>) => {
      if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0)
        return;
      event.preventDefault();
      open.onOpenReview();
    };
    return (
      <li>
        <a className={className} href={REVIEW_ADDRESS} onClick={onReview} data-testid={testId}>
          {body}
        </a>
      </li>
    );
  }
  if (href) {
    return (
      <li>
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

  const onClick = () => {
    if (entry.kind === 'section') {
      const first = MENUS[entry.section.menu].items[0];
      if (first) open.onOpenDoc(first.key);
      return;
    }
    if (entry.action.key === 'inventory') open.onOpenInventory();
    if (entry.action.key === 'account') open.onOpenAccount();
    if (entry.action.key === 'admin') open.onOpenAdmin();
  };
  return (
    <li>
      <button
        type="button"
        className={className}
        onClick={onClick}
        data-testid={testId}
        data-tone={tone}
      >
        {body}
      </button>
    </li>
  );
}
