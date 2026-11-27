/**
 * Does:      Holds the words and the photo of the author card the blog's side opens with, and the byline photo every post
 *            carries (ADR-142, the addendum on the landing views): his badge photo, his name, his role, About's own
 *            sentence on what he builds, and the two doors.
 * Does not:  Draw anything (AuthorCard.tsx and BlogPostCard.tsx do); no React in here.
 * Used by:   AuthorCard.tsx, BlogPostCard.tsx, AuthorCard.test.ts.
 */

/** His badge photo, the 264 px cut the home page's About Steven door already offers. */
export const AUTHOR_PHOTO = '/api/images/badges/steve-264.webp';

/** The card's words: who, what, and the sentence About opens with. */
export const AUTHOR_CARD = {
  name: 'Steven Stout',
  role: 'Lead / Staff .NET engineer · Remote',
  line: 'I have spent twelve years building full stack software in C#, SQL Server, React and Azure.',
  doors: [
    { label: 'About Steven', href: '/?view=about', mock: 'author-about' },
    { label: 'Check my work', href: '/?view=review', mock: 'author-review' },
  ],
} as const;
