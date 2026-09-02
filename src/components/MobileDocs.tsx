import { useRef, useState } from 'react';
import {
  DocDialog,
  DOCS,
  LINKS,
  MENU_ORDER,
  MENUS,
  type DocKey,
  type DocRequest,
} from './DocsMenu';
import { RowIcon } from './SheetIcons';
import styles from './MobileDocs.module.css';

/**
 * The phone header: one hamburger button that opens a dark drawer holding
 * every header menu as a headed section, then Admin, the resume, and the
 * repository. Built from the same MENUS record the desktop dropdowns use, so
 * the two can never drift apart. Every row leads with an icon picked from
 * the doc's kind (ADR-011 addendum). Owns the one doc dialog a phone needs;
 * the drawer closes as a doc opens.
 */
export function MobileDocs({ onOpenAdmin }: { onOpenAdmin: () => void }) {
  const drawerRef = useRef<HTMLDialogElement>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [request, setRequest] = useState<DocRequest | null>(null);

  const openDrawer = () => {
    drawerRef.current?.showModal();
    setDrawerOpen(true);
  };
  const closeDrawer = () => drawerRef.current?.close();

  const openDoc = (key: DocKey) => {
    closeDrawer();
    setRequest((prev) => ({ key, nonce: (prev?.nonce ?? 0) + 1 }));
  };

  return (
    <div className={styles.wrap}>
      <button
        type="button"
        className={styles.hamburger}
        aria-label="Menu"
        aria-haspopup="dialog"
        aria-expanded={drawerOpen}
        onClick={openDrawer}
      >
        <svg viewBox="0 0 20 20" width="20" height="20" aria-hidden="true">
          <path d="M3 5h14M3 10h14M3 15h14" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
        </svg>
      </button>

      <dialog
        ref={drawerRef}
        className={styles.drawer}
        aria-label="Menu"
        onClose={() => setDrawerOpen(false)}
        onClick={(event) => {
          // Native dialog: a click on the backdrop targets the dialog itself.
          if (event.target === drawerRef.current) closeDrawer();
        }}
      >
        <div className={styles.drawerHeader}>
          <span className={styles.brand}>
            <svg className={styles.brandMark} viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
              <path d="M13 2 5 14h5l-2 8 8-12h-5l2-8z" fill="currentColor" />
            </svg>
            The Yard
          </span>
          <button type="button" className={styles.close} onClick={closeDrawer} aria-label="Close">
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
                  className={sub ? `${styles.row} ${styles.subRow}` : styles.row}
                  onClick={() => openDoc(key)}
                >
                  <RowIcon kind={DOCS[key].kind} className={styles.icon} />
                  <span className={styles.label}>{DOCS[key].menuLabel}</span>
                </button>
              ))}
              {variant === 'cicd' && (
                <a className={styles.row} href={LINKS.ciRuns.href} target="_blank" rel="noreferrer">
                  <RowIcon kind="external" className={styles.icon} />
                  <span className={styles.label}>{LINKS.ciRuns.label}</span>
                </a>
              )}
            </section>
          ))}
        </nav>

        <div className={styles.actions}>
          <button
            type="button"
            className={styles.row}
            onClick={() => {
              closeDrawer();
              onOpenAdmin();
            }}
          >
            <RowIcon kind="admin" className={styles.icon} />
            <span className={styles.label}>Admin</span>
          </button>
          <a className={styles.row} href={LINKS.resume.href} target="_blank" rel="noreferrer">
            <RowIcon kind="external" className={styles.icon} />
            <span className={styles.label}>{LINKS.resume.label}</span>
          </a>
          <a className={styles.row} href={LINKS.repo.href} target="_blank" rel="noreferrer">
            <RowIcon kind="external" className={styles.icon} />
            <span className={styles.label}>{LINKS.repo.label}</span>
          </a>
        </div>
      </dialog>

      <DocDialog request={request} />
    </div>
  );
}
