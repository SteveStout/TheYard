import { useEffect, useRef, useState } from 'react';
import { marked } from 'marked';
import styles from './DocsMenu.module.css';

function ExternalIcon() {
  return (
    <svg viewBox="0 0 12 12" width="10" height="10" aria-hidden="true">
      <path
        d="M4.5 1.5h6v6M10.5 1.5 5 7M8 10.5H1.5V4"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.5"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

type DocKey = 'readme' | 'dataflow' | 'projects';

const DOCS: Record<DocKey, { title: string; url: string }> = {
  readme: { title: 'README', url: '/api/docs/readme' },
  dataflow: { title: 'Data Flow', url: '/api/docs/dataflow' },
  projects: { title: 'Projects', url: '/api/docs/projects' },
};

/**
 * The header's About dropdown: view the project docs in-app (markdown,
 * served by the API), open the author's résumé PDF, or visit the repository.
 */
export function DocsMenu() {
  const [menuOpen, setMenuOpen] = useState(false);
  const [activeDoc, setActiveDoc] = useState<DocKey>('readme');
  const [docHtml, setDocHtml] = useState<Partial<Record<DocKey, string>>>({});
  const [docError, setDocError] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);
  const dialogRef = useRef<HTMLDialogElement>(null);

  // Close the dropdown on outside click or Escape.
  useEffect(() => {
    if (!menuOpen) return;
    const onPointerDown = (event: PointerEvent) => {
      if (!menuRef.current?.contains(event.target as Node)) setMenuOpen(false);
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setMenuOpen(false);
    };
    document.addEventListener('pointerdown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('pointerdown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [menuOpen]);

  const openDoc = async (key: DocKey) => {
    setMenuOpen(false);
    setActiveDoc(key);
    setDocError(false);
    dialogRef.current?.showModal();
    if (docHtml[key] !== undefined) return;
    try {
      const response = await fetch(DOCS[key].url);
      if (!response.ok) throw new Error(String(response.status));
      const markdown = await response.text();
      // Our own docs — trusted, repo-authored content.
      const html = await marked.parse(markdown);
      setDocHtml((prev) => ({ ...prev, [key]: html }));
    } catch {
      setDocError(true);
    }
  };

  return (
    <div className={styles.wrap} ref={menuRef}>
      <button
        type="button"
        className={styles.trigger}
        aria-haspopup="menu"
        aria-expanded={menuOpen}
        onClick={() => setMenuOpen((open) => !open)}
      >
        About
        <svg viewBox="0 0 12 8" width="10" height="7" aria-hidden="true">
          <path d="M1 1.5 6 6.5 11 1.5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
        </svg>
      </button>

      {menuOpen && (
        <div className={styles.menu} role="menu">
          <button type="button" className={styles.item} role="menuitem" onClick={() => void openDoc('readme')}>
            Project README
          </button>
          <button type="button" className={styles.item} role="menuitem" onClick={() => void openDoc('dataflow')}>
            Data flow diagram
          </button>
          <button type="button" className={styles.item} role="menuitem" onClick={() => void openDoc('projects')}>
            Project structure
          </button>
          <a
            className={styles.item}
            role="menuitem"
            href="/api/docs/resume"
            target="_blank"
            rel="noreferrer"
            onClick={() => setMenuOpen(false)}
          >
            Steven's résumé (PDF)
            <ExternalIcon />
          </a>
          <a
            className={styles.item}
            role="menuitem"
            href="https://github.com/SteveStout/CodingChallengeOpenLane"
            target="_blank"
            rel="noreferrer"
            onClick={() => setMenuOpen(false)}
          >
            GitHub repository
            <ExternalIcon />
          </a>
        </div>
      )}

      <dialog
        ref={dialogRef}
        className={styles.dialog}
        aria-label={DOCS[activeDoc].title}
        onClick={(event) => {
          // Native dialog: a click on the backdrop targets the dialog itself.
          if (event.target === dialogRef.current) dialogRef.current?.close();
        }}
      >
        <div className={styles.dialogHeader}>
          <h2 className={styles.dialogTitle}>{DOCS[activeDoc].title}</h2>
          <button
            type="button"
            className={styles.close}
            onClick={() => dialogRef.current?.close()}
            aria-label="Close"
          >
            <svg viewBox="0 0 14 14" width="14" height="14" aria-hidden="true">
              <path d="M2 2l10 10M12 2 2 12" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
            </svg>
          </button>
        </div>
        <div className={styles.dialogBody}>
          {docError ? (
            <p className={styles.docError}>
              Couldn't load the {DOCS[activeDoc].title} — is the API running?
            </p>
          ) : docHtml[activeDoc] === undefined ? (
            <p className={styles.docLoading}>Loading…</p>
          ) : (
            <div className={styles.prose} dangerouslySetInnerHTML={{ __html: docHtml[activeDoc] }} />
          )}
        </div>
      </dialog>
    </div>
  );
}
