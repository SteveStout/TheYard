/**
 * Does:      Draws the booking sheet "Book 20 minutes" opens over the page beside the email app (ADR: The review route
 *            gives a reason before it asks, the addendum on no dead ends), as the whole-page picture draws it: the
 *            sentence, the address and the message read from GET /api/links/book when it opens, "Open my email app",
 *            "Copy the address" and "Copy the message" (each reading "Copied" for two seconds), and the LinkedIn link.
 *            A modal dialog on the document window's model: focus inside, Escape, the backdrop and its X close it.
 * Does not:  Hold the address (the API reads it from the site's settings; no page or bundle carries it) or decide when
 *            it opens (the page does, on every "Book 20 minutes").
 * Used by:   ReviewPage.tsx, Landing.tsx.
 */
import { useEffect, useRef, useState } from 'react';
import { request } from '../../../lib/request';
import {
  BOOK_MESSAGE_URL,
  bookMessageFrom,
  messageText,
  SHEET,
  type BookMessage,
} from '../../../lib/reviewBook';
import type { ReviewAction } from '../../../lib/reviewProgress';
import { BOOK_ADDRESS, LINKEDIN_ADDRESS } from '../../../lib/reviewVerdict';
import styles from './BookSheet.module.css';

/** The message read once a page, the first time the sheet opens; null until then or when the read fails. */
let kept: Promise<BookMessage | null> | null = null;
function readMessage(): Promise<BookMessage | null> {
  kept ??= request(BOOK_MESSAGE_URL, undefined, { background: true })
    .then((response) => (response.ok ? (response.json() as Promise<unknown>) : null))
    .then(bookMessageFrom)
    .catch(() => null);
  return kept;
}

/** One copy control: its words, and "Copied" for two seconds after the clipboard takes the text. */
function CopyButton({ label, text }: { label: string; text: string }) {
  const [copied, setCopied] = useState(false);
  useEffect(() => {
    if (!copied) return;
    const id = window.setTimeout(() => setCopied(false), 2000);
    return () => window.clearTimeout(id);
  }, [copied]);
  return (
    <button
      type="button"
      className={`op-pill-secondary ${styles.pill}`}
      onClick={() => {
        // navigator.clipboard is undefined outside a secure context, as the document window's Copy link knows.
        void navigator.clipboard?.writeText(text).then(() => setCopied(true));
      }}
    >
      {copied ? SHEET.copied : label}
    </button>
  );
}

export function BookSheet({
  open,
  onClose,
  onAction,
}: {
  open: boolean;
  onClose: () => void;
  onAction: (action: ReviewAction) => void;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [message, setMessage] = useState<BookMessage | null>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && !dialog.open) {
      dialog.showModal();
      let live = true;
      void readMessage().then((read) => {
        if (live) setMessage(read);
      });
      return () => {
        live = false;
      };
    }
    if (!open && dialog.open) dialog.close();
  }, [open]);

  return (
    <dialog
      ref={dialogRef}
      className={styles.sheet}
      aria-labelledby="book-sheet-title"
      onClose={onClose}
      onClick={(event) => {
        // Native dialog: a click on the backdrop targets the dialog itself.
        if (event.target === dialogRef.current) dialogRef.current?.close();
      }}
      data-testid="book-sheet"
    >
      <div className={`${styles.panel} op-glass op-solid`}>
        <button
          type="button"
          className={styles.x}
          onClick={() => dialogRef.current?.close()}
          aria-label={SHEET.close}
        >
          ×
        </button>
        <h2 id="book-sheet-title" className={styles.title}>
          {SHEET.title}
        </h2>
        <p className={styles.line}>{SHEET.lead}</p>
        {message && (
          <>
            <p className={styles.line}>
              <strong>{SHEET.to}</strong> <span data-testid="book-sheet-to">{message.to}</span>
            </p>
            <pre className={styles.msg} data-testid="book-sheet-message">
              {messageText(message)}
            </pre>
          </>
        )}
        <div className={styles.row}>
          <a
            className={`op-pill-primary ${styles.pill}`}
            href={BOOK_ADDRESS}
            onClick={() => onAction('book')}
            data-action="review.book"
          >
            {SHEET.open}
          </a>
          {message && <CopyButton label={SHEET.copyAddress} text={message.to} />}
          {message && <CopyButton label={SHEET.copyMessage} text={messageText(message)} />}
        </div>
        <p className={styles.line}>
          <a
            className={styles.link}
            href={LINKEDIN_ADDRESS}
            rel="noopener"
            onClick={() => onAction('linkedin')}
            data-action="review.linkedin"
            data-testid="book-sheet-linkedin"
          >
            {SHEET.linkedin}
          </a>
        </p>
      </div>
    </dialog>
  );
}
