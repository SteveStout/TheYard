/**
 * Does:      Draws the home page's "What colleagues say" column (ADR-082, the addendum on the home page's order): the
 *            shared section head with its 03 badge, its words the link to where the recommendations were written, the
 *            three named voices in the answer's own words, and the way to every one of them with the answer's count.
 * Does not:  Read the recommendations (Landing.tsx hands in the one answer the page reads) or pick the three names
 *            (src/lib/voices.ts does).
 * Used by:   Landing.tsx.
 */
import { readAllLine, SECTION_NUMBERS } from '../../../lib/landingMoves';
import { RECOMMENDATIONS_SOURCE, type Recommendation } from '../../../lib/recommendations';
import { RECOMMENDATIONS_ADDRESS } from '../../../lib/reviewVerdict';
import { VOICES } from '../../../lib/voices';
import { SectionHead } from '../../shared/SectionHead';
import { Voice } from '../../shared/Voice';
import styles from '../Landing/Landing.module.css';

export function LandingVoices({
  recommendations,
}: {
  /** The page's one answer, or null before it arrives. */
  recommendations: Recommendation[] | null;
}) {
  return (
    <div className={styles.voices} data-mock="voices" data-testid="landing-voices">
      <SectionHead
        id="landing-voices"
        number={SECTION_NUMBERS.voices}
        title={
          <a
            className={styles.headingLink}
            href={RECOMMENDATIONS_SOURCE}
            target="_blank"
            rel="noreferrer"
          >
            What colleagues say
          </a>
        }
        seen
      />
      {VOICES.home.map((name) => (
        <Voice key={name} name={name} recommendations={recommendations} onFollow={() => {}} />
      ))}
      <a
        className={styles.allRecs}
        href={RECOMMENDATIONS_ADDRESS}
        data-testid="landing-recommendations-all"
      >
        {readAllLine(recommendations?.length ?? null)} <span aria-hidden="true">→</span>
      </a>
    </div>
  );
}
