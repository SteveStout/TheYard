/**
 * Does:      Draws the line over a landing view's title the one way the review route draws it (ADR-142, the addendum on
 *            the landing views): its parts in small capitals, "Steven Stout" first, a gold slash between each two.
 * Does not:  Pick the words (each page hands them in) or draw its look (sales.css, op-eyebrow, does).
 * Used by:   PageHead.tsx, Landing.tsx, RecommendationsPage.tsx.
 */
export function Eyebrow({ parts, testId }: { parts: readonly string[]; testId?: string }) {
  return (
    <p className="op-eyebrow" data-mock="eyebrow" data-testid={testId}>
      {parts.map((part, index) => (
        <span key={`${index}-${part}`}>
          {index > 0 && (
            <span className="op-eyebrow-slash" aria-hidden="true">
              /
            </span>
          )}
          {part}
        </span>
      ))}
    </p>
  );
}
