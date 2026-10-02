// The row of tiles across the top of the Admin tab. Each tile is a link to the card behind its
// number, with a tone word, an optional ring and a sparkline. What a tile says and its tone are
// decided in src/lib/statTiles.ts; this file only draws them. It is its own file because the
// strip is one piece of the page with its own order and its own look.
import type { ReactNode } from 'react';
import { cardForTile } from '../../../lib/bench';
import type { CardSlug } from '../../../lib/workbench';
import { sparkRuns, type StatTile, tileSentence, type TileQuestion } from '../../../lib/statTiles';
import { Ring } from '../../shared/Ring';
import styles from '../AdminPanel/AdminPanel.module.css';
import cardStyles from '../shared/card.module.css';
import stripStyles from '../shared/stat-strip.module.css';
import { followCardLink } from '../AdminPanel/followCardLink';

// #region stat-strip
/** The order the tiles go in: is it up, is it fast, what does it cost, what broke. */
const QUESTION_ORDER: TileQuestion[] = ['up', 'fast', 'cost', 'broke'];

/** The word each tone shows under a tile; a plain tile shows none. */
const TONE_WORD: Record<StatTile['tone'], string | null> = {
  good: 'fine',
  warn: 'worth a look',
  bad: 'needs attention',
  plain: null,
  waiting: 'waiting',
};

/**
 * The strip of tiles across the top (ADR-080). Each tile is a link to the
 * card that explains it. statTiles.ts (plain TypeScript, no React) decides
 * what a tile says and its tone; this code only draws. The tone is shown as a
 * word as well as a colour, so it does not depend on seeing colour.
 */
export function AdminStatStrip({
  tiles,
  onOpenCard,
  toolbar,
  caption,
}: {
  tiles: StatTile[];
  /** Opens the card a tile links to. */
  onOpenCard: (slug: CardSlug) => void;
  /** The row of window buttons the sparklines follow. */
  toolbar: ReactNode;
  /** What the lines under the tiles are of. */
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
                onClick={(event) => followCardLink(event, () => onOpenCard(cardForTile(tile.key)))}
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
