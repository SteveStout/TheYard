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

type DocKey =
  | 'readme'
  | 'dataflow'
  | 'projects'
  | 'hosting'
  | 'adrOrigin'
  | 'adrDocker'
  | 'adrNaming'
  | 'adrPivots';

const DOCS: Record<DocKey, { title: string; menuLabel: string; url: string }> = {
  readme: { title: 'README', menuLabel: 'Project README', url: '/api/docs/readme' },
  dataflow: { title: 'Data Flow', menuLabel: 'Data flow diagram', url: '/api/docs/dataflow' },
  projects: { title: 'Projects', menuLabel: 'Project structure', url: '/api/docs/projects' },
  hosting: { title: 'Hosting', menuLabel: 'Hosting overview', url: '/api/docs/hosting' },
  adrOrigin: { title: 'ADR: Front Door origin', menuLabel: 'ADR: Front Door origin', url: '/api/docs/adr-origin' },
  adrDocker: { title: 'ADR: Docker packaging', menuLabel: 'ADR: Docker packaging', url: '/api/docs/adr-docker' },
  adrNaming: { title: 'ADR: Azure naming', menuLabel: 'ADR: Azure naming', url: '/api/docs/adr-naming' },
  adrPivots: { title: 'ADR: Deployment strategy', menuLabel: 'ADR: Deployment strategy', url: '/api/docs/adr-pivots' },
};

type MenuVariant = 'about' | 'hosting';

type MenuEntry = { key: DocKey; sub?: boolean };

const MENUS: Record<MenuVariant, { label: string; items: MenuEntry[] }> = {
  about: {
    label: 'About',
    items: [{ key: 'readme' }, { key: 'dataflow' }, { key: 'projects' }],
  },
  hosting: {
    label: 'Hosting',
    items: [
      { key: 'hosting' },
      { key: 'adrOrigin', sub: true },
      { key: 'adrDocker', sub: true },
      { key: 'adrNaming', sub: true },
      { key: 'adrPivots', sub: true },
    ],
  },
};

/**
 * A header dropdown that opens repo docs in an in-app dialog (markdown served
 * by the API). The About variant also links the resume PDF and the repository;
 * the Hosting variant collects every Azure, certificate, and deployment
 * decision in one place.
 */
export function DocsMenu({ menu = 'about' }: { menu?: MenuVariant }) {
  const { label, items } = MENUS[menu];
  const [menuOpen, setMenuOpen] = useState(false);
  const [activeDoc, setActiveDoc] = useState<DocKey>(items[0].key);
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
      // Our own docs - trusted, repo-authored content.
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
        {label}
        <svg viewBox="0 0 12 8" width="10" height="7" aria-hidden="true">
          <path d="M1 1.5 6 6.5 11 1.5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
        </svg>
      </button>

      {menuOpen && (
        <div className={styles.menu} role="menu">
          {items.map(({ key, sub }) => (
            <button
              key={key}
              type="button"
              className={sub ? `${styles.item} ${styles.subItem}` : styles.item}
              role="menuitem"
              onClick={() => void openDoc(key)}
            >
              {DOCS[key].menuLabel}
            </button>
          ))}
          {menu === 'about' && (
            <>
              <a
                className={styles.item}
                role="menuitem"
                href="/api/docs/resume"
                target="_blank"
                rel="noreferrer"
                onClick={() => setMenuOpen(false)}
              >
                Steven's resume (PDF)
                <ExternalIcon />
              </a>
              <a
                className={styles.item}
                role="menuitem"
                href="https://github.com/SteveStout/TheYard"
                target="_blank"
                rel="noreferrer"
                onClick={() => setMenuOpen(false)}
              >
                GitHub repository
                <ExternalIcon />
              </a>
            </>
          )}
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
              Couldn't load the {DOCS[activeDoc].title} - is the API running?
            </p>
          ) : docHtml[activeDoc] === undefined ? (
            <p className={styles.docLoading}>Loading...</p>
          ) : (
            <div className={styles.prose} dangerouslySetInnerHTML={{ __html: docHtml[activeDoc] }} />
          )}
        </div>
      </dialog>
    </div>
  );
}
