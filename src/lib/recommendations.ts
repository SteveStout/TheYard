/**
 * Does:      Names the shape /api/recommendations answers and reads an answer into it, the entries and the facts the API works out from them, so every page that shows what colleagues say draws from one wire.
 * Does not:  Fetch (the component does, with the page's own fetch), work out a count or a group, or draw anything.
 * Used by:   Recommendations.tsx, recommendations.test.ts.
 */
import file from '../../data/recommendations.json';

/** One recommendation as the endpoint answers it: the six fields every entry of data/recommendations.json has. */
export type Recommendation = {
  name: string;
  title: string;
  /** The address of their own page on LinkedIn, which their name opens. */
  profile: string;
  relationship: string;
  /** The month and year LinkedIn dates it, as words. */
  when: string;
  /** The recommendation word for word, one string per paragraph. */
  paragraphs: string[];
  /** One sentence of it, as written, for a place with room for one line per person. */
  pull: string;
};

/** The page the recommendations were written on, which both headings open. */
export const RECOMMENDATIONS_SOURCE =
  'https://www.linkedin.com/in/stevenwstout/details/recommendations/';

/** Where the words come from on the site: the one endpoint, read by the landing page's column and by About Steven's. */
export const RECOMMENDATIONS_URL = '/api/recommendations';

/** How many blank places the column draws before the answer arrives, so nothing moves when it does: one per entry in the file. */
export const RECOMMENDATION_PLACES = file.recommendations.length;

/** One group of writers as the API answers it: who they were to Steven, in the pages' order. */
export type RecommendationGroup = { key: string; title: string; lead: string; names: string[] };

/** The whole answer: the entries and the facts the API works out from them, never stated anywhere. */
export type RecommendationsAnswer = {
  entries: Recommendation[];
  count: number;
  from: string;
  to: string;
  groups: RecommendationGroup[];
};

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
      !text(entry.profile) ||
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
      profile: entry.profile,
      relationship: entry.relationship,
      when: entry.when,
      paragraphs: entry.paragraphs,
      pull: entry.pull,
    });
  }
  return entries;
}
// #endregion recommendations-from

const group = (value: unknown): value is RecommendationGroup => {
  if (typeof value !== 'object' || value === null) return false;
  const g = value as Record<string, unknown>;
  return (
    text(g.key) && text(g.title) && text(g.lead) && Array.isArray(g.names) && g.names.every(text)
  );
};

/**
 * The entries and the derived facts in an answer, or null when either is not the endpoint's shape. The count,
 * the years and the groups are read as the API sends them; nothing here counts or groups.
 */
export function recommendationsAnswerFrom(answer: unknown): RecommendationsAnswer | null {
  const entries = recommendationsFrom(answer);
  if (entries === null) return null;
  const facts = answer as { count?: unknown; from?: unknown; to?: unknown; groups?: unknown };
  if (typeof facts.count !== 'number' || !Array.isArray(facts.groups) || !facts.groups.every(group))
    return null;
  return {
    entries,
    count: facts.count,
    from: typeof facts.from === 'string' ? facts.from : '',
    to: typeof facts.to === 'string' ? facts.to : '',
    groups: facts.groups,
  };
}
