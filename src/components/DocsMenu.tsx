import { useEffect, useRef, useState } from 'react';
import { marked } from 'marked';
import styles from './DocsMenu.module.css';

// #region doc-links
// Links in a served document lead out of the app (GitHub, a diagram page), so
// they open in a new tab and the dialog stays where the reader was. The docs
// name the live domain in full, which keeps them right on GitHub; here the same
// links are made relative, so a checkout on localhost opens its own diagram
// page and not the live one (ADR-020).
const SITE = 'https://theyard.stevenstout.biz/';
marked.use({
  hooks: {
    postprocess(html: string) {
      return html
        .replaceAll(`href="${SITE}`, 'href="/')
        .replace(/<a href="(?!#)/g, '<a target="_blank" rel="noopener" href="');
    },
  },
});
// #endregion doc-links

export type DocKey =
  | 'readme'
  | 'dataflow'
  | 'projects'
  | 'hosting'
  | 'adrOrigin'
  | 'adrDocker'
  | 'adrNaming'
  | 'adrPivots'
  | 'adrEdgeCost'
  | 'adrLinux'
  | 'bicep'
  | 'cicd'
  | 'adrPipeline'
  | 'practices'
  | 'adrVersioning'
  | 'adrDocs'
  | 'adrObservability'
  | 'adrPhone'
  | 'changelog'
  | 'adrChangelog'
  | 'adrSidebar'
  | 'adrLiveSamples'
  | 'adrCaching'
  | 'adrPalette'
  | 'adrReview'
  | 'adrProgram'
  | 'adrReact'
  | 'adrDiagrams';

/** What a doc is. The phone drawer picks each row's icon from this (ADR-011 addendum). */
export type DocKind = 'overview' | 'adr' | 'infra' | 'changelog';

/**
 * Every doc the sidebar can open. The url's last segment is the slug the API
 * looks up in api/TheBlock.Api/DocsCatalog.cs; DocsCatalogTests holds the two
 * lists to each other (ADR-017).
 */
// #region docs-record
export const DOCS: Record<DocKey, { title: string; menuLabel: string; url: string; kind: DocKind }> = {
  readme: { title: 'README', menuLabel: 'Project README', url: '/api/docs/readme', kind: 'overview' },
  dataflow: { title: 'Data Flow', menuLabel: 'Data flow diagram', url: '/api/docs/dataflow', kind: 'overview' },
  projects: { title: 'Projects', menuLabel: 'Project structure', url: '/api/docs/projects', kind: 'overview' },
  hosting: { title: 'Hosting', menuLabel: 'Hosting overview', url: '/api/docs/hosting', kind: 'overview' },
  adrOrigin: { title: 'ADR: Front Door origin', menuLabel: 'ADR: Front Door origin', url: '/api/docs/adr-origin', kind: 'adr' },
  adrDocker: { title: 'ADR: Docker packaging', menuLabel: 'ADR: Docker packaging', url: '/api/docs/adr-docker', kind: 'adr' },
  adrNaming: { title: 'ADR: Azure naming', menuLabel: 'ADR: Azure naming', url: '/api/docs/adr-naming', kind: 'adr' },
  adrPivots: { title: 'ADR: Deployment strategy', menuLabel: 'ADR: Deployment strategy', url: '/api/docs/adr-pivots', kind: 'adr' },
  adrEdgeCost: { title: 'ADR: Edge deploy economics', menuLabel: 'ADR: Edge deploy economics', url: '/api/docs/adr-edge-economics', kind: 'adr' },
  adrLinux: { title: 'ADR: Linux over Windows', menuLabel: 'ADR: Linux over Windows', url: '/api/docs/adr-linux', kind: 'adr' },
  bicep: { title: 'Infrastructure (Bicep)', menuLabel: 'Infrastructure (Bicep)', url: '/api/docs/bicep', kind: 'infra' },
  cicd: { title: 'CI/CD', menuLabel: 'CI/CD overview', url: '/api/docs/cicd', kind: 'overview' },
  adrPipeline: { title: 'ADR: The deploy pipeline', menuLabel: 'ADR: The deploy pipeline', url: '/api/docs/adr-pipeline', kind: 'adr' },
  practices: { title: 'Best Practices', menuLabel: 'Best practices overview', url: '/api/docs/practices', kind: 'overview' },
  adrVersioning: { title: 'ADR: Version in the footer', menuLabel: 'ADR: Version in the footer', url: '/api/docs/adr-versioning', kind: 'adr' },
  adrDocs: { title: 'ADR: Docs and testing', menuLabel: 'ADR: Docs and testing', url: '/api/docs/adr-docs', kind: 'adr' },
  adrObservability: { title: 'ADR: Observability', menuLabel: 'ADR: Observability (Admin tab)', url: '/api/docs/adr-observability', kind: 'adr' },
  adrPhone: { title: 'ADR: The phone header', menuLabel: 'ADR: The phone header', url: '/api/docs/adr-phone', kind: 'adr' },
  changelog: { title: 'Changelog', menuLabel: 'Version history', url: '/api/docs/changelog', kind: 'changelog' },
  adrChangelog: { title: 'ADR: The changelog', menuLabel: 'ADR: The changelog', url: '/api/docs/adr-changelog', kind: 'adr' },
  adrSidebar: { title: 'ADR: The sidebar', menuLabel: 'ADR: The sidebar', url: '/api/docs/adr-sidebar', kind: 'adr' },
  adrLiveSamples: { title: 'ADR: Live code samples', menuLabel: 'ADR: Live code samples', url: '/api/docs/adr-live-samples', kind: 'adr' },
  adrCaching: { title: 'ADR: Cache headers', menuLabel: 'ADR: Cache headers', url: '/api/docs/adr-caching', kind: 'adr' },
  adrPalette: { title: 'ADR: The palette', menuLabel: 'ADR: The palette', url: '/api/docs/adr-palette', kind: 'adr' },
  adrReview: { title: 'ADR: The staff review', menuLabel: 'ADR: The staff review', url: '/api/docs/adr-review', kind: 'adr' },
  adrProgram: { title: 'ADR: Program.cs, explained', menuLabel: 'ADR: Program.cs, explained', url: '/api/docs/adr-program', kind: 'adr' },
  adrReact: { title: 'ADR: The React configuration, explained', menuLabel: 'ADR: The React configuration, explained', url: '/api/docs/adr-react', kind: 'adr' },
  adrDiagrams: { title: 'ADR: Diagram pages', menuLabel: 'ADR: Diagram pages', url: '/api/docs/adr-diagrams', kind: 'adr' },
};
// #endregion docs-record

export type MenuVariant = 'about' | 'hosting' | 'cicd' | 'practices' | 'changelog';

export type MenuEntry = { key: DocKey; sub?: boolean };

export const MENUS: Record<MenuVariant, { label: string; items: MenuEntry[] }> = {
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
      { key: 'adrEdgeCost', sub: true },
      { key: 'adrLinux', sub: true },
      { key: 'bicep', sub: true },
    ],
  },
  cicd: {
    label: 'CI/CD',
    items: [{ key: 'cicd' }, { key: 'adrPipeline', sub: true }],
  },
  practices: {
    label: 'Best Practices',
    items: [
      { key: 'practices' },
      { key: 'adrVersioning', sub: true },
      { key: 'adrDocs', sub: true },
      { key: 'adrObservability', sub: true },
      { key: 'adrPhone', sub: true },
      { key: 'adrChangelog', sub: true },
      { key: 'adrSidebar', sub: true },
      { key: 'adrLiveSamples', sub: true },
      { key: 'adrCaching', sub: true },
      { key: 'adrPalette', sub: true },
      { key: 'adrReview', sub: true },
      { key: 'adrProgram', sub: true },
      { key: 'adrReact', sub: true },
      { key: 'adrDiagrams', sub: true },
    ],
  },
  // #region menu-changelog
  /** One item on purpose: one file, one sentence per version (ADR-012). */
  changelog: {
    label: 'Changelog',
    items: [{ key: 'changelog' }],
  },
  // #endregion menu-changelog
};

// #region MENU_ORDER
/** Section order, top to bottom. The sidebar renders from it in both of its shapes (ADR-013). */
export const MENU_ORDER: MenuVariant[] = ['hosting', 'cicd', 'practices', 'changelog', 'about'];
// #endregion MENU_ORDER

/** Links that sit beside the docs in the sidebar. */
export const LINKS = {
  ciRuns: { label: 'CI runs on GitHub', href: 'https://github.com/SteveStout/TheYard/actions' },
  resume: { label: "Steven's resume (PDF)", href: '/api/docs/resume' },
  repo: { label: 'GitHub repository', href: 'https://github.com/SteveStout/TheYard' },
} as const;

/** One open request: the nonce lets the same doc be reopened after a close. */
export type DocRequest = { key: DocKey; nonce: number };

/**
 * The in-app doc viewer: a native modal dialog that fetches the markdown the
 * API serves, renders it, and caches it per doc. Every sidebar row opens its
 * doc through the one instance the sidebar owns; onClose lets the sidebar
 * drop the row's current marker.
 */
export function DocDialog({ request, onClose }: { request: DocRequest | null; onClose?: () => void }) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [docHtml, setDocHtml] = useState<Partial<Record<DocKey, string>>>({});
  const [docError, setDocError] = useState(false);
  /** Same content as docHtml, readable inside the effect without a stale closure. */
  const cache = useRef<Partial<Record<DocKey, string>>>({});
  const activeDoc: DocKey = request?.key ?? 'readme';

  useEffect(() => {
    if (!request) return;
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
    setDocError(false);
    const { key } = request;
    if (cache.current[key] !== undefined) return;
    fetch(DOCS[key].url)
      .then(async (response) => {
        if (!response.ok) throw new Error(String(response.status));
        const markdown = await response.text();
        // Our own docs - trusted, repo-authored content.
        const html = await marked.parse(markdown);
        cache.current[key] = html;
        setDocHtml((prev) => ({ ...prev, [key]: html }));
      })
      .catch(() => setDocError(true));
  }, [request]);

  return (
    <dialog
      ref={dialogRef}
      className={styles.dialog}
      aria-label={DOCS[activeDoc].title}
      onClose={onClose}
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
  );
}
