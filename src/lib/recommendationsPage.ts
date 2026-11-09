/**
 * Does:      Turns the recommendations answer into what the recommendations page draws: each group a section holding its entries in
 *            the file's order, each entry's anchor on the page, the index of every name, and the three tiles' words.
 * Does not:  Fetch, count or group (the API does both and the answer carries them), or draw anything.
 * Used by:   RecommendationsPage.tsx, recommendationsPage.test.ts, App.tsx.
 */
import type { Recommendation, RecommendationsAnswer } from './recommendations';

/** One section of the page: a group from the API with its entries, in the order the file lists them. */
export type RecommendationSection = {
  key: string;
  title: string;
  lead: string;
  entries: Recommendation[];
};

/** The label the Links slice records when a tagged link opens this page (TaggedRequests.RecommendationsView). */
export const RECOMMENDATIONS_REPORT_PATH = '/recommendations';

/** The anchor a person's card answers to on the page, from their name alone: lower case, words joined by hyphens. */
export function anchorOf(name: string): string {
  return name
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}

/** The address of a person's card on the page, for the rail's "Read the whole thing" links. */
export function cardAddress(name: string): string {
  return `/?view=recommendations#${anchorOf(name)}`;
}

/** The page's sections: each group the API answered, its names resolved to their entries; a name with no entry is left out. */
export function sectionsOf(answer: RecommendationsAnswer): RecommendationSection[] {
  const byName = new Map(answer.entries.map((entry) => [entry.name, entry]));
  return answer.groups
    .map((group) => ({
      key: group.key,
      title: group.title,
      lead: group.lead,
      entries: group.names.map((name) => byName.get(name)).filter((entry) => entry !== undefined),
    }))
    .filter((section) => section.entries.length > 0);
}

/** The "On this page" index: every name in the order the sections draw them, each with its anchor. */
export function indexOf(
  sections: readonly RecommendationSection[]
): { name: string; anchor: string }[] {
  return sections.flatMap((section) =>
    section.entries.map((entry) => ({ name: entry.name, anchor: anchorOf(entry.name) }))
  );
}

/** The page's title, its count read from the answer. */
export function titleOf(answer: RecommendationsAnswer): string {
  return `${answer.count} people who worked with Steven`;
}

/** The three tiles above the groups: the count, the span and the vantage points, each a figure and a line. */
export function tilesOf(
  answer: RecommendationsAnswer
): { label: string; figure: string; line: string }[] {
  const span = answer.from === answer.to ? answer.from : `${answer.from} to ${answer.to}`;
  return [
    { label: 'Recommendations', figure: String(answer.count), line: 'all on LinkedIn' },
    { label: 'Written', figure: span, line: 'by the year LinkedIn dates them' },
    {
      label: 'Vantage points',
      figure: String(answer.groups.length),
      line: 'where the writers stood',
    },
  ];
}
