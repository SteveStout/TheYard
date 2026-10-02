/**
 * Does:      Draws the window a document opens in: asks the API for it, renders it, lays it in panels, and offers its link.
 * Does not:  Decide which document is open (the sidebar and useNavigation.ts do), or hold any document's words.
 * Used by:   SideNav.tsx.
 */
import { useEffect, useRef, useState } from 'react';
import styles from './DocDialog.module.css';
import prose from './DocProse.module.css';
import swatches from './DocSwatches.module.css';
import panels from './DocPanels.module.css';
import author from './AuthorPage.module.css';
import blocks from './StyleBlocks.module.css';
import { useLiveBlocks } from './useLiveBlocks';
import { layoutDocument } from '../lib/docLayout';
import { ICON } from '../lib/icons';
import { DOCS, type DocKey } from './documents';

/**
 * The document's sheets, one job each (ADR-019, the addendum on the stylesheets). Each scopes its
 * rules under its own .prose, so the element that holds the document carries all five; the
 * reading panel's frost is scoped under the window as well, so the window carries that sheet's .dialog.
 */
const PROSE = `${prose.prose} ${swatches.prose} ${panels.prose} ${author.prose} ${blocks.prose}`;
const WINDOW = `${styles.dialog} ${panels.dialog} ${styles.dialogGround}`;

/**
 * One open request. It is an object rather than a bare key because reopening
 * the same document after closing it has to read as a new request, and a fresh
 * object is exactly that: identity is the whole mechanism, so there is nothing
 * to increment and nothing to keep in sync.
 */
export type DocRequest = { key: DocKey };

/**
 * The in-app doc viewer: a native modal dialog that fetches the markdown the
 * API serves, renders it, and caches it per doc. Every sidebar row opens its
 * doc through the one instance the sidebar owns; onClose lets the sidebar
 * drop the row's current marker.
 */
export function DocDialog({
  request,
  onClose,
  onOpenDoc,
}: {
  request: DocRequest | null;
  onClose?: () => void;
  /** Opens another document in this window: an in-library link on the Style pages (useLiveBlocks.tsx). */
  onOpenDoc?: (key: DocKey) => void;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const proseRef = useRef<HTMLDivElement>(null);
  const [docHtml, setDocHtml] = useState<Partial<Record<DocKey, string>>>({});
  // #region derived-error
  // The request that failed, not a boolean saying something did. A boolean has
  // to be cleared when the next document opens, and clearing it means a
  // setState inside the effect that opens the dialog, which starts a second
  // render for no reason. Holding the request makes the flag derivable: the
  // next one is a different object, so the old failure stops matching by
  // itself.
  const [failedRequest, setFailedRequest] = useState<DocRequest | null>(null);
  const docError = request !== null && failedRequest === request;
  // #endregion derived-error
  /** Same content as docHtml, readable inside the effect without a stale closure. */
  const cache = useRef<Partial<Record<DocKey, string>>>({});
  // #region copy-link
  // Every record has an address, and a page that did not say so hid it. A
  // feature nobody can find is a feature nobody has. The label carries the
  // whole state: idle, copied, or the browser refusing, which happens on an
  // insecure origin and when the clipboard permission is denied, and in that
  // case the address bar already holds the link and says so.
  const [copied, setCopied] = useState<'no' | 'yes' | 'refused'>('no');
  useEffect(() => {
    if (copied === 'no') return;
    const id = window.setTimeout(() => setCopied('no'), 2500);
    return () => window.clearTimeout(id);
  }, [copied]);
  // #endregion copy-link
  const activeDoc: DocKey = request?.key ?? 'readme';

  useEffect(() => {
    const dialog = dialogRef.current;
    // No request means no record showing, and that has to close a dialog that
    // is open rather than only decline to open one. Before records had
    // addresses the only way to close this was the dialog's own X, Escape or
    // backdrop, so the state always moved dialog-first; now the browser's Back
    // button moves it the other way and the dialog follows.
    if (!request) {
      if (dialog?.open) dialog.close();
      return;
    }
    if (dialog && !dialog.open) dialog.showModal();
    const { key } = request;
    if (cache.current[key] !== undefined) return;
    // #region renderer-on-demand
    // The document and the code that renders it are asked for together, not one
    // after the other: the renderer (marked and the highlighter, the two
    // heaviest things the frontend carries) is a chunk of its own that the
    // inventory never needs (ADR: Code that reads like code, addendum), and on
    // 28 September its request started only once the document's had finished,
    // 775 ms in. Both now start at once, and the Author page's layout with them.
    // The browser keeps each chunk for a year like every other hashed file, and
    // the page fetches the renderer ahead when it is idle (src/lib/prefetch.ts).
    Promise.all([
      fetch(DOCS[key].url).then(async (response) => {
        if (!response.ok) throw new Error(String(response.status));
        return response.text();
      }),
      import('../lib/markdown'),
      key === 'author' ? import('../lib/author') : Promise.resolve(null),
    ])
      .then(async ([markdown, { renderDocument }, author]) => {
        const rendered = await renderDocument(markdown);
        // #endregion renderer-on-demand
        // The Author page has a shape of its own: panels, blocks, buttons and
        // photographs. Every other document takes the same panels by its
        // headings, so one look covers the whole library.
        const html = author !== null ? author.layoutAuthor(rendered) : layoutDocument(rendered);
        cache.current[key] = html;
        setDocHtml((prev) => ({ ...prev, [key]: html }));
      })
      .catch(() => setFailedRequest(request));
  }, [request]);

  useLiveBlocks(proseRef, request ? docHtml[activeDoc] : undefined, onOpenDoc);

  return (
    <dialog
      ref={dialogRef}
      className={
        activeDoc === 'author'
          ? `${WINDOW} ${styles.dialogWide} op-glass op-sheet op-inset`
          : `${WINDOW} op-glass op-sheet op-inset`
      }
      aria-label={DOCS[activeDoc].title}
      onClose={onClose}
      onClick={(event) => {
        // Native dialog: a click on the backdrop targets the dialog itself.
        if (event.target === dialogRef.current) dialogRef.current?.close();
      }}
    >
      {/* Every document stands on the ribbon ground: the page's own, read through the clear
          sheet since 1.0.3.23, where the dialog carried a copy of its own from 1.0.2.0. */}
      <div className={styles.dialogHeader}>
        <h2 className={styles.dialogTitle}>{DOCS[activeDoc].title}</h2>
        <button
          type="button"
          className={styles.copyLink}
          onClick={() => {
            // navigator.clipboard is undefined outside a secure context, not a
            // promise that rejects, so optional chaining here silently did
            // nothing and left the button saying "Copy link" forever. Absent
            // and refused are the same answer to the reader.
            const clipboard = navigator.clipboard;
            if (!clipboard) {
              setCopied('refused');
              return;
            }
            void clipboard
              .writeText(window.location.href)
              .then(() => setCopied('yes'))
              .catch(() => setCopied('refused'));
          }}
        >
          {copied === 'yes'
            ? 'Link copied'
            : copied === 'refused'
              ? 'It is in the address bar'
              : 'Copy link'}
        </button>
        <button
          type="button"
          className={styles.close}
          onClick={() => dialogRef.current?.close()}
          aria-label="Close"
        >
          <svg viewBox="0 0 14 14" width={ICON.sm} height={ICON.sm} aria-hidden="true">
            <path
              d="M2 2l10 10M12 2 2 12"
              stroke="currentColor"
              strokeWidth={ICON.stroke}
              strokeLinecap="round"
            />
          </svg>
        </button>
      </div>
      <div className={styles.dialogBody}>
        {docError ? (
          <p className={styles.docError}>
            Couldn't load the {DOCS[activeDoc].title} - is the API running?
          </p>
        ) : docHtml[activeDoc] === undefined ? (
          <p className={styles.docLoading}>Loading...</p>
        ) : (
          <div
            ref={proseRef}
            className={PROSE}
            dangerouslySetInnerHTML={{ __html: docHtml[activeDoc] }}
          />
        )}
      </div>
    </dialog>
  );
}
