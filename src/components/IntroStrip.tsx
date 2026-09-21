import { useState } from 'react';
import { INTRO, dismissIntro, introDismissed, type IntroLink } from '../lib/intro';
import styles from './IntroStrip.module.css';

/**
 * One sentence and four links over the inventory (ADR: The glass look, the
 * addendum on who the first screen is for), for somebody who arrived from a
 * post with no idea what this is. It never covers the inventory, it is a
 * region with a name so a screen reader can skip it, and once dismissed it
 * stays dismissed in that browser. What it says is in src/lib/intro.ts.
 */
function browserStore(): Storage | undefined {
  try {
    return window.localStorage;
  } catch {
    return undefined;
  }
}

export function IntroStrip({
  resumeHref,
  onOpenAuthor,
  onOpenBuilt,
  onOpenAdmin,
}: {
  resumeHref: string;
  onOpenAuthor: () => void;
  onOpenBuilt: () => void;
  onOpenAdmin: () => void;
}) {
  const [dismissed, setDismissed] = useState(() => introDismissed(browserStore()));
  if (dismissed) return null;

  const act: Record<Exclude<IntroLink, 'resume'>, () => void> = {
    author: onOpenAuthor,
    built: onOpenBuilt,
    admin: onOpenAdmin,
  };

  return (
    <aside className={styles.strip} aria-label="About this site" data-testid="intro-strip">
      <p className={styles.sentence}>{INTRO.sentence}</p>
      <div className={styles.links}>
        {INTRO.links.map((link) =>
          link.key === 'resume' ? (
            <a
              key={link.key}
              className={styles.link}
              href={resumeHref}
              target="_blank"
              rel="noreferrer"
              data-testid="intro-resume"
            >
              {link.label}
            </a>
          ) : (
            <button
              key={link.key}
              type="button"
              className={styles.link}
              onClick={act[link.key]}
              data-testid={`intro-${link.key}`}
            >
              {link.label}
            </button>
          )
        )}
        <button
          type="button"
          className={styles.dismiss}
          aria-label={INTRO.dismiss}
          onClick={() => {
            dismissIntro(browserStore());
            setDismissed(true);
          }}
          data-testid="intro-dismiss"
        >
          <svg viewBox="0 0 20 20" width="16" height="16" aria-hidden="true">
            <path
              d="M5 5l10 10M15 5 5 15"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
            />
          </svg>
        </button>
      </div>
    </aside>
  );
}
