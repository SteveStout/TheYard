import { describe, expect, it } from 'vitest';
import {
  bookMessageFrom,
  dockOn,
  FORWARD,
  forwardText,
  messageText,
  SHEET,
  shareAddress,
} from './reviewBook';

describe('no dead ends on the review route (ADR: The review route gives a reason before it asks, the addendum on no dead ends)', () => {
  it('words the booking sheet and the send line as the picture does', () => {
    expect(SHEET.title).toBe('Book 20 minutes');
    expect(SHEET.lead).toBe(
      'Your email app should have opened with this message. If it did not, copy it and send it from any mailbox.'
    );
    expect([SHEET.open, SHEET.copyAddress, SHEET.copyMessage, SHEET.linkedin]).toEqual([
      'Open my email app',
      'Copy the address',
      'Copy the message',
      'Or message me on LinkedIn',
    ]);
    expect(FORWARD.line).toBe('Send this page to your recruiter or your team');
    expect(FORWARD.title).toBe('Steven Stout, Lead / Staff .NET engineer');
    expect(forwardText('https://x.example/?view=review')).toBe(
      'His site is the running system. Two minutes: https://x.example/?view=review'
    );
  });

  it('reads the booking message only when every part is text and the address is there', () => {
    const message = { to: 'a@example.com', subject: 'Hi', body: 'There' };
    expect(bookMessageFrom(message)).toEqual(message);
    expect(bookMessageFrom({ ...message, to: ' ' })).toBeNull();
    expect(bookMessageFrom({ ...message, body: 3 })).toBeNull();
    expect(bookMessageFrom(null)).toBeNull();
    expect(bookMessageFrom('a@example.com')).toBeNull();
  });

  it('shows and copies the message as its subject line, a blank line and its body, with the line breaks it has', () => {
    expect(
      messageText({ to: 'a@example.com', subject: '20 minutes', body: 'Hi,\r\n\r\n1.\r\n2.\r\n' })
    ).toBe('Subject: 20 minutes\n\nHi,\n\n1.\n2.');
  });

  it('shares the route with its for value and the visitor’s code, and nothing else', () => {
    expect(shareAddress('https://x.example', null, null)).toBe('https://x.example/?view=review');
    expect(shareAddress('https://x.example', 'ai', null)).toBe(
      'https://x.example/?view=review&for=ai'
    );
    expect(shareAddress('https://x.example', 'ai', 'k7f2qa')).toBe(
      'https://x.example/?view=review&for=ai&r=k7f2qa'
    );
  });

  it('turns the dock on once the first screen’s buttons have left the top of the window', () => {
    expect(dockOn(null)).toBe(false);
    expect(dockOn({ bottom: 400 })).toBe(false);
    expect(dockOn({ bottom: 0 })).toBe(false);
    expect(dockOn({ bottom: -1 })).toBe(true);
  });
});
