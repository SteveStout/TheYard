import { useRef, useState } from 'react';
import {
  DocDialog,
  DOCS,
  ExternalIcon,
  LINKS,
  MENU_ORDER,
  MENUS,
  type DocKey,
  type DocRequest,
} from './DocsMenu';
import styles from './MobileDocs.module.css';

/**
 * The phone header: one hamburger button that opens a full-height sheet
 * holding every header menu as a headed section, then Admin, the resume,
 * and the repository. Built from the same MENUS record the desktop
 * dropdowns use, so the two can never drift apart. Owns the one doc dialog
 * a phone needs; the sheet closes as a doc opens.
 */
export function MobileDocs({ onOpenAdmin }: { onOpenAdmin: () => void }) {
  const sheetRef = useRef<HTMLDialogElement>(null);
  const [sheetOpen, setSheetOpen] = useState(false);
  const [request, setRequest] = useState<DocRequest | null>(null);

  const openSheet = () => {
    sheetRef.current?.showModal();
    setSheetOpen(true);
  };
  const closeSheet = () => sheetRef.current?.close();

  const openDoc = (key: DocKey) => {
    closeSheet();
    setRequest((prev) => ({ key, nonce: (prev?.nonce ?? 0) + 1 }));
  };

  return (
    <div className={styles.wrap}>
      <button
        type="button"
        className={styles.hamburger}
        aria-label="Menu"
        aria-haspopup="dialog"
        aria-expanded={sheetOpen}
        onClick={openSheet}
      >
        <svg viewBox="0 0 20 20" width="20" height="20" aria-hidden="true">
          <path d="M3 5h14M3 10h14M3 15h14" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
        </svg>
      </button>

      <dialog
        ref={sheetRef}
        className={styles.sheet}
        aria-label="Menu"
        onClose={() => setSheetOpen(false)}
        onClick={(event) => {
          // Native dialog: a click on the backdrop targets the dialog itself.
          if (event.target === sheetRef.current) closeSheet();
        }}
      >
        <div className={styles.sheetHeader}>
          <h2 className={styles.sheetTitle}>Menu</h2>
          <button type="button" className={styles.close} onClick={closeSheet} aria-label="Close">
            <svg viewBox="0 0 14 14" width="14" height="14" aria-hidden="true">
              <path d="M2 2l10 10M12 2 2 12" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
            </svg>
          </button>
        </div>

        <nav className={styles.sections} aria-label="Project documents">
          {MENU_ORDER.map((variant) => (
            <section key={variant} className={styles.section}>
              <h3 className={styles.sectionTitle}>{MENUS[variant].label}</h3>
              {MENUS[variant].items.map(({ key, sub }) => (
                <button
                  key={key}
                  type="button"
                  className={sub ? `${styles.entry} ${styles.subEntry}` : styles.entry}
                  onClick={() => openDoc(key)}
                >
                  {DOCS[key].menuLabel}
                </button>
              ))}
              {variant === 'cicd' && (
                <a className={styles.entry} href={LINKS.ciRuns.href} target="_blank" rel="noreferrer">
                  {LINKS.ciRuns.label}
                  <ExternalIcon />
                </a>
              )}
            </section>
          ))}
        </nav>

        <div className={styles.actions}>
          <button
            type="button"
            className={styles.action}
            onClick={() => {
              closeSheet();
              onOpenAdmin();
            }}
          >
            Admin
          </button>
          <a className={styles.action} href={LINKS.resume.href} target="_blank" rel="noreferrer">
            {LINKS.resume.label}
            <ExternalIcon />
          </a>
          <a className={styles.action} href={LINKS.repo.href} target="_blank" rel="noreferrer">
            {LINKS.repo.label}
            <ExternalIcon />
          </a>
        </div>
      </dialog>

      <DocDialog request={request} />
    </div>
  );
}
