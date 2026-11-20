/**
 * Does:      Draws one case card under "01 The work" (ADR: The sales pages set the site's look), as the sales1120 picture
 *            draws it: the body on glass, its eyebrow, heading, three bullets and its doors on the last line, and the dark
 *            panel beside it (under it on a phone), the stack as four facts or the week read live. No card is a dead end:
 *            each door reports its action; the proof opens Quality in place as the first screen's button did, the
 *            questions door scrolls to them, a post door opens the blog in place.
 * Does not:  Hold the words (reviewCases.ts does), read the API (the page hands the rows in) or draw the look
 *            (sales.css, op-case, does).
 * Used by:   ReviewWork.tsx.
 */
import type { MouseEvent } from 'react';
import { caseDoors, caseHeading, type CaseDoor, type ReviewCase } from '../../../lib/reviewCases';
import type { NowRow } from '../../../lib/nowPanel';
import type { ReviewAction } from '../../../lib/reviewProgress';
import { NowPanel } from '../../shared/NowPanel';

export function CaseCard({
  entry,
  testsFigure,
  rows,
  asOf,
  onAction,
  onDoor,
}: {
  entry: ReviewCase;
  /** The first screen's live tests figure, which TheYard's heading takes. */
  testsFigure: string;
  /** The live panel's rows, for the card whose panel is read live. */
  rows: readonly NowRow[];
  /** "as of HH:MM" when the newest read of the pulse failed. */
  asOf: string | null;
  onAction: (action: ReviewAction) => void;
  /** What a door does on a plain click besides its report: the proof and a post open in place, the questions scroll. */
  onDoor: (door: CaseDoor, event: MouseEvent<HTMLAnchorElement>) => void;
}) {
  const doors = caseDoors(entry);
  return (
    <article
      className="op-case"
      id={entry.id}
      aria-labelledby={`${entry.id}-title`}
      data-mock="case"
    >
      <div className="op-case-body">
        <p className="op-case-eyebrow">{entry.eyebrow}</p>
        <h3 id={`${entry.id}-title`}>{caseHeading(entry, testsFigure)}</h3>
        <ul>
          {entry.bullets.map((bullet) => (
            <li key={bullet}>{bullet}</li>
          ))}
        </ul>
        <p className="op-doors">
          {doors.map((door) => (
            <a
              key={door.label}
              className="op-door"
              href={door.href}
              onClick={(event) => {
                onAction(door.action);
                onDoor(door, event);
              }}
              data-action={`review.${door.action}`}
              data-mock="case-door"
            >
              {door.label}
              <i aria-hidden="true">{door.arrow}</i>
            </a>
          ))}
        </p>
      </div>
      {entry.panel.live ? (
        <NowPanel
          as="aside"
          title={entry.panel.title}
          live
          rows={rows}
          asOf={asOf}
          hook="case-panel"
        />
      ) : (
        <NowPanel
          as="aside"
          title={entry.panel.title}
          facts={entry.panel.facts}
          hook="case-panel"
        />
      )}
    </article>
  );
}
