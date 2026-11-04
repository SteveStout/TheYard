/**
 * Does:      Draws the landing page's banners under Inventory and Author, from the site map, the resume first: the
 *            resume as a link to the PDF in a new tab, the review route as a link that opens its view in place.
 * Does not:  Decide which actions are banners or their order (siteMapLanding.ts does) or route a view (the caller does).
 * Used by:   Landing.tsx.
 */
import type { MouseEvent } from 'react';
import { REVIEW_ADDRESS } from '../../../lib/reviewStops';
import type { LandingTile } from '../../../lib/siteMap';
import { LINKS } from '../../../library/sections';
import { NavGlyph } from '../../shared/SheetIcons';
import tiles from '../Landing/Landing.module.css';
import styles from './LandingBanners.module.css';

/** A plain click opens the view in place; a click with a key held or another button keeps the browser's own way. */
function plainClick(event: MouseEvent): boolean {
  return event.button === 0 && !event.metaKey && !event.ctrlKey && !event.shiftKey && !event.altKey;
}

export function LandingBanners({
  banners,
  onOpenReview,
}: {
  banners: LandingTile[];
  /** Opens the review route, the way the blog's band opens the blog. */
  onOpenReview: () => void;
}) {
  return (
    <ul
      className={styles.banners}
      aria-label="The resume, and the site in ten minutes"
      data-testid="landing-banners"
    >
      {banners.map((entry) => {
        if (entry.kind !== 'action') return null;
        const { key, label, blurb, icon } = entry.action;
        const review = key === 'review';
        return (
          <li key={key}>
            <a
              className={`${styles.banner} op-glass`}
              href={review ? REVIEW_ADDRESS : LINKS.resume.href}
              target={review ? undefined : '_blank'}
              rel={review ? undefined : 'noreferrer'}
              data-testid={`landing-banner-${key}`}
              onClick={(event) => {
                if (!review || !plainClick(event)) return;
                event.preventDefault();
                onOpenReview();
              }}
            >
              <span className={`${tiles.badge} ${styles.badge}`}>
                <NavGlyph icon={icon} size={30} />
              </span>
              <span className={styles.text}>
                <span className={tiles.title}>{label}</span>
                <span className={tiles.blurb}>{blurb}</span>
              </span>
            </a>
          </li>
        );
      })}
    </ul>
  );
}
