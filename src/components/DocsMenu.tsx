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
  | 'adrDiagrams'
  | 'adrTests'
  | 'adrGrouping'
  | 'adrErrors'
  | 'adrTelemetry'
  | 'adrSearch'
  | 'adrKeyboard'
  | 'adrBidders'
  | 'adrStyle'
  | 'adrRecords'
  | 'adrExceptions'
  | 'adrVersionSource'
  | 'adrMethod'
  | 'adrStore'
  | 'adrEf'
  | 'adrA11yCheck'
  | 'adrPhotos'
  | 'adrAccounts'
  | 'adrIdentity'
  | 'adrSqlServer'
  | 'adrDataFirst'
  | 'adrProviders'
  | 'adrExemptions'
  | 'adrSqlVisible'
  | 'adrInterceptors'
  | 'adrSelfReview'
  | 'aiDevelopment'
  | 'architecture'
  | 'style';

/** What a doc is. The phone drawer picks each row's icon from this (ADR-011 addendum). */
export type DocKind = 'overview' | 'adr' | 'infra' | 'changelog';

/**
 * Every doc the sidebar can open. The url's last segment is the slug the API
 * looks up in api/TheBlock.Api/DocsCatalog.cs; DocsCatalogTests holds the two
 * lists to each other (ADR-017).
 */
// #region docs-record
// One record, one place. The URL's last segment is the slug the API looks up
// in DocsCatalog.cs, and a test fails if the two lists ever disagree, so a
// document can never appear in the menu without being servable, or the other
// way around (ADR-017).
export const DOCS: Record<
  DocKey,
  { title: string; menuLabel: string; url: string; kind: DocKind; number?: string }
> = {
  readme: {
    title: 'README',
    menuLabel: 'Project README',
    url: '/api/docs/readme',
    kind: 'overview',
  },
  dataflow: {
    title: 'Data Flow',
    menuLabel: 'Data flow diagram',
    url: '/api/docs/dataflow',
    kind: 'overview',
  },
  projects: {
    title: 'Projects',
    menuLabel: 'Project structure',
    url: '/api/docs/projects',
    kind: 'overview',
  },
  hosting: {
    title: 'Hosting',
    menuLabel: 'Hosting overview',
    url: '/api/docs/hosting',
    kind: 'overview',
  },
  adrOrigin: {
    title: 'ADR: Front Door origin',
    menuLabel: 'ADR: Front Door origin',
    url: '/api/docs/adr-origin',
    kind: 'adr',
    number: '001',
  },
  adrDocker: {
    title: 'ADR: Docker packaging',
    menuLabel: 'ADR: Docker packaging',
    url: '/api/docs/adr-docker',
    kind: 'adr',
    number: '002',
  },
  adrNaming: {
    title: 'ADR: Azure naming',
    menuLabel: 'ADR: Azure naming',
    url: '/api/docs/adr-naming',
    kind: 'adr',
    number: '003',
  },
  adrPivots: {
    title: 'ADR: Deployment strategy',
    menuLabel: 'ADR: Deployment strategy',
    url: '/api/docs/adr-pivots',
    kind: 'adr',
    number: '004',
  },
  adrEdgeCost: {
    title: 'ADR: Edge deploy economics',
    menuLabel: 'ADR: Edge deploy economics',
    url: '/api/docs/adr-edge-economics',
    kind: 'adr',
    number: '007',
  },
  adrLinux: {
    title: 'ADR: Linux over Windows',
    menuLabel: 'ADR: Linux over Windows',
    url: '/api/docs/adr-linux',
    kind: 'adr',
    number: '008',
  },
  bicep: {
    title: 'Infrastructure (Bicep)',
    menuLabel: 'Infrastructure (Bicep)',
    url: '/api/docs/bicep',
    kind: 'infra',
  },
  cicd: { title: 'CI/CD', menuLabel: 'CI/CD overview', url: '/api/docs/cicd', kind: 'overview' },
  adrPipeline: {
    title: 'ADR: The deploy pipeline',
    menuLabel: 'ADR: The deploy pipeline',
    url: '/api/docs/adr-pipeline',
    kind: 'adr',
    number: '009',
  },
  practices: {
    title: 'Best Practices',
    menuLabel: 'Best practices overview',
    url: '/api/docs/practices',
    kind: 'overview',
  },
  adrVersioning: {
    title: 'ADR: Version in the footer',
    menuLabel: 'ADR: Version in the footer',
    url: '/api/docs/adr-versioning',
    kind: 'adr',
    number: '005',
  },
  adrDocs: {
    title: 'ADR: Docs and testing',
    menuLabel: 'ADR: Docs and testing',
    url: '/api/docs/adr-docs',
    kind: 'adr',
    number: '006',
  },
  adrObservability: {
    title: 'ADR: Observability',
    menuLabel: 'ADR: Observability (Admin tab)',
    url: '/api/docs/adr-observability',
    kind: 'adr',
    number: '010',
  },
  adrPhone: {
    title: 'ADR: The phone header',
    menuLabel: 'ADR: The phone header',
    url: '/api/docs/adr-phone',
    kind: 'adr',
    number: '011',
  },
  changelog: {
    title: 'Changelog',
    menuLabel: 'Version history',
    url: '/api/docs/changelog',
    kind: 'changelog',
  },
  adrChangelog: {
    title: 'ADR: The changelog',
    menuLabel: 'ADR: The changelog',
    url: '/api/docs/adr-changelog',
    kind: 'adr',
    number: '012',
  },
  adrSidebar: {
    title: 'ADR: The sidebar',
    menuLabel: 'ADR: The sidebar',
    url: '/api/docs/adr-sidebar',
    kind: 'adr',
    number: '013',
  },
  adrLiveSamples: {
    title: 'ADR: Live code samples',
    menuLabel: 'ADR: Live code samples',
    url: '/api/docs/adr-live-samples',
    kind: 'adr',
    number: '014',
  },
  adrCaching: {
    title: 'ADR: Cache headers',
    menuLabel: 'ADR: Cache headers',
    url: '/api/docs/adr-caching',
    kind: 'adr',
    number: '015',
  },
  adrPalette: {
    title: 'ADR: The palette',
    menuLabel: 'ADR: The palette',
    url: '/api/docs/adr-palette',
    kind: 'adr',
    number: '016',
  },
  adrReview: {
    title: 'ADR: The staff review',
    menuLabel: 'ADR: The staff review',
    url: '/api/docs/adr-review',
    kind: 'adr',
    number: '017',
  },
  adrProgram: {
    title: 'ADR: Program.cs, explained',
    menuLabel: 'ADR: Program.cs, explained',
    url: '/api/docs/adr-program',
    kind: 'adr',
    number: '018',
  },
  adrReact: {
    title: 'ADR: The React configuration, explained',
    menuLabel: 'ADR: The React configuration, explained',
    url: '/api/docs/adr-react',
    kind: 'adr',
    number: '019',
  },
  adrDiagrams: {
    title: 'ADR: Diagram pages',
    menuLabel: 'ADR: Diagram pages',
    url: '/api/docs/adr-diagrams',
    kind: 'adr',
    number: '020',
  },
  adrTests: {
    title: 'ADR: The tests, explained',
    menuLabel: 'ADR: The tests, explained',
    url: '/api/docs/adr-tests',
    kind: 'adr',
    number: '021',
  },
  architecture: {
    title: 'App Architecture',
    menuLabel: 'Architecture overview',
    url: '/api/docs/architecture',
    kind: 'overview',
  },
  style: {
    title: 'Coding and Commenting Style',
    menuLabel: 'Coding and commenting style',
    url: '/api/docs/style',
    kind: 'overview',
  },
  adrGrouping: {
    title: 'ADR: App Architecture section',
    menuLabel: 'ADR: App Architecture section',
    url: '/api/docs/adr-grouping',
    kind: 'adr',
    number: '022',
  },
  adrErrors: {
    title: 'ADR: Error handling',
    menuLabel: 'ADR: Error handling',
    url: '/api/docs/adr-errors',
    kind: 'adr',
    number: '023',
  },
  adrTelemetry: {
    title: 'ADR: Telemetry',
    menuLabel: 'ADR: Telemetry',
    url: '/api/docs/adr-telemetry',
    kind: 'adr',
    number: '024',
  },
  adrSearch: {
    title: 'ADR: The search index',
    menuLabel: 'ADR: The search index',
    url: '/api/docs/adr-search',
    kind: 'adr',
    number: '025',
  },
  adrKeyboard: {
    title: 'ADR: Keyboard and screen reader',
    menuLabel: 'ADR: Keyboard access',
    url: '/api/docs/adr-keyboard',
    kind: 'adr',
    number: '026',
  },
  adrBidders: {
    title: 'ADR: Competing bidders',
    menuLabel: 'ADR: Competing bidders',
    url: '/api/docs/adr-bidders',
    kind: 'adr',
    number: '027',
  },
  adrStyle: {
    title: 'ADR: Style, enforced',
    menuLabel: 'ADR: Style, enforced',
    url: '/api/docs/adr-style',
    kind: 'adr',
    number: '028',
  },
  adrRecords: {
    title: 'ADR: The Decision Records index',
    menuLabel: 'ADR: The Decision Records index',
    url: '/api/docs/adr-records',
    kind: 'adr',
    number: '029',
  },
  adrExceptions: {
    title: 'ADR: The exception handler',
    menuLabel: 'ADR: The exception handler',
    url: '/api/docs/adr-exceptions',
    kind: 'adr',
    number: '030',
  },
  adrVersionSource: {
    title: 'ADR: The version comes from the changelog',
    menuLabel: 'ADR: The version comes from the changelog',
    url: '/api/docs/adr-version-source',
    kind: 'adr',
    number: '031',
  },
  adrMethod: {
    title: 'ADR: Saying how it was built',
    menuLabel: 'ADR: Saying how it was built',
    url: '/api/docs/adr-method',
    kind: 'adr',
    number: '032',
  },
  adrStore: {
    title: 'ADR: The relational store',
    menuLabel: 'ADR: The relational store',
    url: '/api/docs/adr-store',
    kind: 'adr',
    number: '033',
  },
  adrEf: {
    title: 'ADR: Entity Framework, explained',
    menuLabel: 'ADR: Entity Framework, explained',
    url: '/api/docs/adr-ef',
    kind: 'adr',
    number: '034',
  },
  adrA11yCheck: {
    title: 'ADR: The accessibility check',
    menuLabel: 'ADR: The accessibility check',
    url: '/api/docs/adr-a11y-check',
    kind: 'adr',
    number: '035',
  },
  adrPhotos: {
    title: 'ADR: Responsive photos',
    menuLabel: 'ADR: Responsive photos',
    url: '/api/docs/adr-photos',
    kind: 'adr',
    number: '036',
  },
  adrAccounts: {
    title: 'ADR: Accounts and per-user bids',
    menuLabel: 'ADR: Accounts and per-user bids',
    url: '/api/docs/adr-accounts',
    kind: 'adr',
    number: '037',
  },
  adrIdentity: {
    title: 'ADR: Identity and the session token, explained',
    menuLabel: 'ADR: Identity, explained',
    url: '/api/docs/adr-identity',
    kind: 'adr',
    number: '038',
  },
  adrSqlServer: {
    title: 'ADR: The SQL Server backend',
    menuLabel: 'ADR: The SQL Server backend',
    url: '/api/docs/adr-sql-server',
    kind: 'adr',
    number: '039',
  },
  adrDataFirst: {
    title: 'ADR: Data first, and the database in source control',
    menuLabel: 'ADR: Data first',
    url: '/api/docs/adr-data-first',
    kind: 'adr',
    number: '040',
  },
  adrProviders: {
    title: 'ADR: Two providers and a SQL project, explained',
    menuLabel: 'ADR: Two providers, explained',
    url: '/api/docs/adr-providers',
    kind: 'adr',
    number: '041',
  },
  adrExemptions: {
    title: 'ADR: The exemption that hid a contrast failure',
    menuLabel: 'ADR: Exemptions that hide',
    url: '/api/docs/adr-exemptions',
    kind: 'adr',
    number: '042',
  },
  adrSqlVisible: {
    title: 'ADR: What the database is actually doing',
    menuLabel: 'ADR: What the database is doing',
    url: '/api/docs/adr-sql-visible',
    kind: 'adr',
    number: '043',
  },
  adrInterceptors: {
    title: 'ADR: Watching your own SQL, explained',
    menuLabel: 'ADR: Watching your own SQL',
    url: '/api/docs/adr-interceptors',
    kind: 'adr',
    number: '044',
  },
  adrSelfReview: {
    title: 'ADR: Reviewing my own work, and what that found',
    menuLabel: 'ADR: Reviewing my own work',
    url: '/api/docs/adr-self-review',
    kind: 'adr',
    number: '045',
  },
  aiDevelopment: {
    title: 'How this was built',
    menuLabel: 'How this was built',
    url: '/api/docs/ai-development',
    kind: 'overview',
  },
};
// #endregion docs-record

export type MenuVariant =
  'about' | 'architecture' | 'hosting' | 'cicd' | 'practices' | 'records' | 'changelog';

export type MenuEntry = { key: DocKey; sub?: boolean };

export const MENUS: Record<
  MenuVariant,
  { label: string; items: MenuEntry[]; collapsible?: boolean }
> = {
  about: {
    label: 'About',
    items: [{ key: 'readme' }, { key: 'aiDevelopment' }],
  },
  // #region architecture-menu
  architecture: {
    label: 'App Architecture',
    items: [
      { key: 'architecture' },
      { key: 'style', sub: true },
      { key: 'dataflow', sub: true },
      { key: 'projects', sub: true },
    ],
  },
  // #endregion architecture-menu
  hosting: {
    label: 'Hosting',
    items: [{ key: 'hosting' }, { key: 'bicep', sub: true }],
  },
  cicd: {
    label: 'CI/CD',
    items: [{ key: 'cicd' }],
  },
  practices: {
    label: 'Best Practices',
    items: [{ key: 'practices' }],
  },
  // #region records-menu
  /**
   * Every decision record, in the order they were decided, numbered to match
   * the file each one serves. They used to hang off the four topic sections as
   * sub-rows, which put eighteen under Best Practices alone and turned the
   * sidebar into a wall. Twenty-seven of anything is an index, not a submenu.
   */
  records: {
    label: 'Decision Records',
    collapsible: true,
    items: [
      { key: 'adrOrigin' },
      { key: 'adrDocker' },
      { key: 'adrNaming' },
      { key: 'adrPivots' },
      { key: 'adrVersioning' },
      { key: 'adrDocs' },
      { key: 'adrEdgeCost' },
      { key: 'adrLinux' },
      { key: 'adrPipeline' },
      { key: 'adrObservability' },
      { key: 'adrPhone' },
      { key: 'adrChangelog' },
      { key: 'adrSidebar' },
      { key: 'adrLiveSamples' },
      { key: 'adrCaching' },
      { key: 'adrPalette' },
      { key: 'adrReview' },
      { key: 'adrProgram' },
      { key: 'adrReact' },
      { key: 'adrDiagrams' },
      { key: 'adrTests' },
      { key: 'adrGrouping' },
      { key: 'adrErrors' },
      { key: 'adrTelemetry' },
      { key: 'adrSearch' },
      { key: 'adrKeyboard' },
      { key: 'adrBidders' },
      { key: 'adrStyle' },
      { key: 'adrRecords' },
      { key: 'adrExceptions' },
      { key: 'adrVersionSource' },
      { key: 'adrMethod' },
      { key: 'adrStore' },
      { key: 'adrEf' },
      { key: 'adrA11yCheck' },
      { key: 'adrPhotos' },
      { key: 'adrAccounts' },
      { key: 'adrIdentity' },
      { key: 'adrSqlServer' },
      { key: 'adrDataFirst' },
      { key: 'adrProviders' },
      { key: 'adrExemptions' },
      { key: 'adrSqlVisible' },
      { key: 'adrInterceptors' },
      { key: 'adrSelfReview' },
    ],
  },
  // #endregion records-menu
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
export const MENU_ORDER: MenuVariant[] = [
  'architecture',
  'hosting',
  'cicd',
  'practices',
  'records',
  'changelog',
  'about',
];
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
export function DocDialog({
  request,
  onClose,
}: {
  request: DocRequest | null;
  onClose?: () => void;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [docHtml, setDocHtml] = useState<Partial<Record<DocKey, string>>>({});
  // #region derived-error
  // The nonce that failed, not a boolean saying something did. A boolean has to
  // be cleared when the next document opens, and clearing it means a setState
  // inside the effect that opens the dialog, which starts a second render for
  // no reason. Holding the nonce makes the flag derivable: the next request
  // carries a new one, so the old failure stops matching by itself.
  const [failedNonce, setFailedNonce] = useState<number | null>(null);
  const docError = request !== null && failedNonce === request.nonce;
  // #endregion derived-error
  /** Same content as docHtml, readable inside the effect without a stale closure. */
  const cache = useRef<Partial<Record<DocKey, string>>>({});
  const activeDoc: DocKey = request?.key ?? 'readme';

  useEffect(() => {
    if (!request) return;
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
    const { key, nonce } = request;
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
      .catch(() => setFailedNonce(nonce));
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
            <path
              d="M2 2l10 10M12 2 2 12"
              stroke="currentColor"
              strokeWidth="2"
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
          <div className={styles.prose} dangerouslySetInnerHTML={{ __html: docHtml[activeDoc] }} />
        )}
      </div>
    </dialog>
  );
}
