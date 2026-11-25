/**
 * Does:      Holds the About page's address, its eyebrow and the label its opens are reported under, and cuts the author
 *            document's first panel into what the page's hero draws: the words, the document's own profile links, and
 *            the photograph (ADR: The sidebar, the addendum on About Steven as a page).
 * Does not:  Read or render the document (useAuthorParts.ts and author.ts do) or draw anything; no React in here.
 * Used by:   AboutPage.tsx, App.tsx, aboutPage.test.ts.
 */

/** The page's own address. */
export const ABOUT_ADDRESS = '/?view=about';

/** The label the Links slice records when a tagged link opens the page: About Steven's one name (TaggedRequests.PageViews). */
export const ABOUT_REPORT_PATH = '/about';

/** The line over the page, the recommendations page's eyebrow with this page's name. */
export const ABOUT_EYEBROW = 'Who and why · About Steven';

/** One of the document's own links under the hero's words. */
export type HeroLink = { href: string; label: string };

/** The hero's three parts. */
export type Hero = { words: string; links: HeroLink[]; photo: string | null };

const FIGURE = /<figure class="author-photo"[\s\S]*?<\/figure>/;
const BUTTONS = /<p class="author-buttons">([\s\S]*?)<\/p>/;

/**
 * The first panel cut into the hero's parts: its words without the row of buttons, the row's links but the resume
 * (the gold Resume (PDF) button opens the same resume), and its first photograph.
 * @param intro the first panel as authorParts gives it
 */
export function heroOf(intro: string): Hero {
  const row = BUTTONS.exec(intro)?.[1] ?? '';
  const links = Array.from(row.matchAll(/<a [^>]*href="([^"]*)"[^>]*>([^<]*)<\/a>/g), (link) => ({
    href: link[1],
    label: link[2],
  })).filter((link) => !/resume/i.test(link.label));
  return {
    words: intro.replace(FIGURE, '').replace(BUTTONS, ''),
    links,
    photo: FIGURE.exec(intro)?.[0] ?? null,
  };
}

/** The page's title, the document's own first heading as words, or the eyebrow's name when it has none. */
export function titleOf(top: string): string {
  const heading = /<h1[^>]*>([\s\S]*?)<\/h1>/.exec(top)?.[1];
  return heading === undefined ? 'About Steven' : heading.replace(/<[^>]+>/g, '').trim();
}
