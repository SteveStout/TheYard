/**
 * A card's live line under its dials (ADR: Live dials where the source allows): what the source reads, how often, and
 * its newest reading, moving at the source's grain while the card is open; or, for a card with no live source, the
 * one quiet sentence that says so; the age is against the server's own clock. The tones stay the kept minutes', and
 * this line never colours the card.
 */
import { useLive } from '../../../hooks/useLive';
import { liveWords } from '../../../lib/live';
import cardStyles from '../shared/card.module.css';

export function LivePanel({ card }: { card: string }) {
  const report = useLive(card);
  return (
    <p
      className={cardStyles.muted}
      data-testid="card-live"
      data-available={report?.available ? 'yes' : 'no'}
    >
      {liveWords(report, report?.as_of_ms ?? 0)}
    </p>
  );
}
