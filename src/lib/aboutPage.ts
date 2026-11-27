/**
 * Does:      Holds the About page's address, its eyebrow and the label its opens are reported under, and cuts the author
 *            document's first panel into what the page's head draws: the title, the words, the document's own profile
 *            links, and the photograph, the first paragraph hooked as the role line and the last as the person beside
 *            the work (ADR: The sidebar, the addendum on About Steven as a page; ADR-142, the addendum on the landing
 *            views).
 * Does not:  Read or render the document (useAuthorParts.ts and author.ts do) or draw anything; no React in here.
 * Used by:   AboutPage.tsx, App.tsx, aboutPage.test.ts.
 */

import { words } from './aboutWeave';

/** The page's own address. */
export const ABOUT_ADDRESS = '/?view=about';

/** The label the Links slice records when a tagged link opens the page: About Steven's one name (TaggedRequests.PageViews). */
export const ABOUT_REPORT_PATH = '/about';

/** The line over the title, the review route's eyebrow with this page's name: the old title, About Steven, lives here. */
export const ABOUT_EYEBROW = [
  'Steven Stout',
  'About',
  'Lead / Staff .NET engineer',
  'Remote',
] as const;

/** One of the document's own links under the hero's words. */
export type HeroLink = { href: string; label: string };

/** The head's parts: the panel's own title, its words (the role line first, the person beside the work last), its links and photo. */
export type Hero = { title: string; words: string; links: HeroLink[]; photo: string | null };

const FIGURE = /<figure class="author-photo"[\s\S]*?<\/figure>/;
const BUTTONS = /<p class="author-buttons">([\s\S]*?)<\/p>/;
const PARAGRAPH = /<p>(?![\s\S]*<p>)/;
const HEADING = /<h2[^>]*>([\s\S]*?)<\/h2>/;

/**
 * The words with their last plain paragraph hooked as the person beside the work, when there are three: the bold line,
 * the work, then the life outside it (the data-mock name the About picture's check reads).
 */
function personal(words: string): string {
  return (words.match(/<p>/g) ?? []).length < 3
    ? words
    : words
        .replace(PARAGRAPH, '<p data-mock="intro-personal">')
        .replace('<p>', '<p data-mock="role-line">');
}

/**
 * The first panel cut into the head's parts: its heading as the title, its words without the heading or the row of
 * buttons, the row's links but the resume (the gold Resume (PDF) button opens the same resume), and its first photograph.
 * @param intro the first panel as authorParts gives it
 */
export function heroOf(intro: string): Hero {
  const row = BUTTONS.exec(intro)?.[1] ?? '';
  const links = Array.from(row.matchAll(/<a [^>]*href="([^"]*)"[^>]*>([^<]*)<\/a>/g), (link) => ({
    href: link[1],
    label: link[2],
  })).filter((link) => !/resume/i.test(link.label));
  return {
    title: words(HEADING.exec(intro)?.[1] ?? ''),
    words: personal(intro.replace(FIGURE, '').replace(BUTTONS, '').replace(HEADING, '')),
    links,
    photo: FIGURE.exec(intro)?.[0] ?? null,
  };
}

/** The page's title, the document's own first heading as words, or the eyebrow's name when it has none. */
export function titleOf(top: string): string {
  const heading = /<h1[^>]*>([\s\S]*?)<\/h1>/.exec(top)?.[1];
  return heading === undefined ? 'About Steven' : heading.replace(/<[^>]+>/g, '').trim();
}
