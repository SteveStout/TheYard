/**
 * Does:      Holds what keeps "Book 20 minutes" from being a dead end (ADR: The review route gives a reason before it
 *            asks, the addendum on no dead ends): the booking sheet's words, the read of its message and its checks, the
 *            message as one block to copy, the send line's words and the address it shares, and the dock's watch.
 * Does not:  Read anything, open a dialog or touch the clipboard (BookSheet.tsx, ReviewClose.tsx and ReviewDock.tsx
 *            do); no React in here.
 * Used by:   BookSheet.tsx, ReviewClose.tsx, ReviewDock.tsx, ReviewPage.tsx, reviewBook.test.ts.
 */
import { VERDICT } from './reviewVerdict';

/** The booking sheet's words, word for word from the picture. */
export const SHEET = {
  title: 'Book 20 minutes',
  lead: 'Your email app should have opened with this message. If it did not, copy it and send it from any mailbox.',
  to: 'To:',
  open: 'Open my email app',
  copyAddress: 'Copy the address',
  copyMessage: 'Copy the message',
  copied: 'Copied',
  linkedin: 'Or message me on LinkedIn',
  close: 'Close',
} as const;

/** Where the sheet reads its message: the same Links:Book address /api/go/book redirects to, in three parts. */
export const BOOK_MESSAGE_URL = '/api/links/book';

/** The booking message as GET /api/links/book answers it. */
export type BookMessage = { to: string; subject: string; body: string };

/** An answer read into the message, or null when it is not one: every part must be text and the address not empty. */
export function bookMessageFrom(answer: unknown): BookMessage | null {
  if (typeof answer !== 'object' || answer === null) return null;
  const { to, subject, body } = answer as Record<string, unknown>;
  return typeof to === 'string' &&
    to.trim() !== '' &&
    typeof subject === 'string' &&
    typeof body === 'string'
    ? { to, subject, body }
    : null;
}

/** The message as one block, the way the sheet shows it and copies it: its subject line, a blank line, its body. */
export function messageText(message: BookMessage): string {
  return `Subject: ${message.subject}\n\n${message.body.replace(/\r\n/g, '\n').trimEnd()}`;
}

/** The send line's words, and what it shares, from the picture's copy block. */
export const FORWARD = {
  line: 'Send this page to your recruiter or your team',
  copied: 'Link copied',
  /** Where the clipboard is refused: the document window's own words for the same answer. */
  refused: 'It is in the address bar',
  /** "Steven Stout, Lead / Staff .NET engineer", from the first screen's own name line. */
  title: `${VERDICT.eyebrow[0]}, ${VERDICT.eyebrow[1]}`,
} as const;

/** The text the send line shares, with the page's own address in it. */
export function forwardText(address: string): string {
  return `His site is the running system. Two minutes: ${address}`;
}

/**
 * The address the send line shares: this route on this site, with its for value when the page was opened with one,
 * and the visitor's own code when the page was opened with one, so a second device opening it counts under the same
 * code. Nothing else the address carried goes with it.
 */
export function shareAddress(origin: string, forValue: string | null, code: string | null): string {
  const params = new URLSearchParams({ view: 'review' });
  if (forValue) params.set('for', forValue);
  if (code) params.set('r', code);
  return `${origin}/?${params.toString()}`;
}

/** The dock is on once the first screen's button row has scrolled up out of the window, and off while it is in it. */
export function dockOn(row: { bottom: number } | null): boolean {
  return row !== null && row.bottom < 0;
}
