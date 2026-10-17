/**
 * Does:      Names the shape /api/recommendations answers and reads an answer into it, so both pages that show what colleagues say draw from one wire.
 * Does not:  Fetch (the component does, with the page's own fetch) or draw anything.
 * Used by:   Recommendations.tsx, recommendations.test.ts.
 */

/** One recommendation as the endpoint answers it: the six fields every entry of data/recommendations.json has. */
export type Recommendation = {
  name: string;
  title: string;
  relationship: string;
  /** The month and year LinkedIn dates it, as words. */
  when: string;
  /** The recommendation word for word, one string per paragraph. */
  paragraphs: string[];
  /** One sentence of it, as written, for a place with room for one line per person. */
  pull: string;
};

/** Where the words come from on the site: the one endpoint, read by the landing page's column and by About Steven's. */
export const RECOMMENDATIONS_URL = '/api/recommendations';

/** How many blank places the column draws before the answer arrives, so nothing moves when it does. */
export const RECOMMENDATION_PLACES = 10;

const text = (value: unknown): value is string => typeof value === 'string' && value.length > 0;
const paragraphs = (value: unknown): value is string[] =>
  Array.isArray(value) && value.length > 0 && value.every(text);

// #region recommendations-from
/**
 * The entries in an answer, or null when the answer is not the endpoint's shape: a list is read only
 * if every entry carries its six fields, so a half-shaped answer is a failure the column says out
 * loud rather than a card with a hole in it. An empty list is a valid answer (empty is valid, null
 * is the error).
 */
export function recommendationsFrom(answer: unknown): Recommendation[] | null {
  if (typeof answer !== 'object' || answer === null) return null;
  const list = (answer as { recommendations?: unknown }).recommendations;
  if (!Array.isArray(list)) return null;
  const entries: Recommendation[] = [];
  for (const item of list) {
    if (typeof item !== 'object' || item === null) return null;
    const entry = item as Record<string, unknown>;
    if (
      !text(entry.name) ||
      !text(entry.title) ||
      !text(entry.relationship) ||
      !text(entry.when) ||
      !text(entry.pull) ||
      !paragraphs(entry.paragraphs)
    ) {
      return null;
    }
    entries.push({
      name: entry.name,
      title: entry.title,
      relationship: entry.relationship,
      when: entry.when,
      paragraphs: entry.paragraphs,
      pull: entry.pull,
    });
  }
  return entries;
}
// #endregion recommendations-from
