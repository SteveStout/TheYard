/**
 * Does:      Says what each sidebar section holds: its documents in order (MENUS), and the rows that open in a new tab (LINKS, ABOUT_PAGE, API_REFERENCE, DIAGRAMS).
 * Does not:  Order the sections or pick their icons (src/lib/siteMap.ts does), or hold a document itself (records.ts and pages.ts do).
 * Used by:   SideNav.tsx, Landing.tsx, Header.tsx, sections.test.ts.
 */
import type { MenuVariant } from '../lib/siteMap';
import type { DocKey } from './documents';

// The section names (MenuVariant), and their order, live in the site map
// (src/lib/siteMap.ts), which the sidebar and the landing page are both drawn from.
export type MenuEntry = { key: DocKey; sub?: boolean };

/** A row that leaves the app in a new tab rather than opening a document in the dialog. */
export type MenuLink = { label: string; href: string };

/** Links that sit beside the docs in the sidebar. */
export const LINKS = {
  ciRuns: { label: 'CI runs on GitHub', href: 'https://github.com/SteveStout/TheYard/actions' },
  resume: { label: "Steven's resume (PDF)", href: '/api/docs/resume' },
  repo: { label: 'GitHub repository', href: 'https://github.com/SteveStout/TheYard' },
} as const;

/** The page about him, served by the API at /about, first in the About section. */
export const ABOUT_PAGE: MenuLink = { label: 'About Steven Stout', href: '/about' };

// #region api-reference
/**
 * The API's description of itself, two rows that leave the app in a new tab
 * (ADR: The API describes itself): the reference page a person reads, and the
 * document a tool reads. Links rather than documents because neither is
 * markdown: the page is Scalar's, served by the API, and the document is JSON.
 */
export const API_REFERENCE: readonly MenuLink[] = [
  { label: 'Browse the API reference', href: '/api/reference' },
  { label: 'The OpenAPI document (JSON)', href: '/api/openapi/v1.json' },
];
// #endregion api-reference

// #region diagrams
/**
 * Every drawing in the catalogue, on its own page (ADR: Every diagram opens on
 * its own page, the addendum on the section). The order is the order a reader
 * meets the system: the whole, the data, the schema, the two sites, the two
 * stores, then the look. The server's DocumentationCatalog.Diagrams is the authority for which
 * drawings exist, and a test holds this list to it, so a drawing cannot have a
 * page without a row or a row without a page.
 */
export const DIAGRAMS: readonly MenuLink[] = [
  { label: 'Infrastructure', href: '/api/docs/diagrams/infrastructure' },
  { label: 'Data flow', href: '/api/docs/diagrams/dataflow' },
  { label: 'The database', href: '/api/docs/diagrams/erd' },
  { label: 'The two sites', href: '/api/docs/diagrams/two-sites' },
  { label: 'SQL Server vs Cosmos DB', href: '/api/docs/diagrams/sql-vs-cosmos' },
  { label: 'The UI, layer by layer', href: '/api/docs/diagrams/ui-architecture' },
];
// #endregion diagrams

export const MENUS: Record<
  MenuVariant,
  { label: string; lead?: readonly MenuLink[]; items: MenuEntry[]; links?: readonly MenuLink[] }
> = {
  /**
   * About opens with the page about him (api/TheYard.Api/AboutPage.cs): served by the API
   * like the diagrams, so it leaves the app in a new tab like every served page, and it is
   * the first row because it is what a reader who searched his name came for.
   */
  about: {
    label: 'About',
    lead: [ABOUT_PAGE],
    items: [{ key: 'readme' }, { key: 'aiDevelopment' }],
  },
  /**
   * The person behind the site, as the last section, right under About (ADR:
   * The sidebar, the addendum on the author's section). One page, and it is
   * a served document like every other, so the page sweep checks it and the
   * house tests read it; what makes it a page rather than a letter is
   * src/lib/author.ts, which gives the rendered words their shape.
   */
  author: {
    label: 'Author',
    items: [{ key: 'author' }],
  },
  // #region architecture-menu
  architecture: {
    label: 'App Architecture',
    items: [
      { key: 'startHere' },
      { key: 'architecture' },
      { key: 'style', sub: true },
      { key: 'dataflow', sub: true },
      { key: 'projects', sub: true },
    ],
  },
  // #endregion architecture-menu
  /**
   * The HTTP surface, as a section of its own right under the architecture,
   * so a reader who came to see the API finds it without opening anything
   * (ADR: The API describes itself).
   */
  apiReference: {
    label: 'API Reference',
    items: [],
    links: API_REFERENCE,
  },
  // #region stores-menu
  /**
   * The two stores, side by side, as a section of its own beside Hosting: the
   * subject is as large as where the site runs, and a reader should not have
   * to know which record holds the comparison (ADR: The sidebar, addendum).
   * The records the page draws on stay in the Decision Records index.
   */
  stores: {
    label: 'SQL vs Cosmos DB',
    items: [{ key: 'sqlVsCosmos' }],
  },
  // #endregion stores-menu
  /**
   * The lowest resources and the millisecond speeds, as a section of its own
   * for the same reason the stores have one: it is the subject a reader asks
   * about first, and it should not have to be found inside a record. Two
   * overviews open it since 1.0.0.145, the machines and then the page they
   * serve, ahead of the page that holds what each change moved.
   */
  performance: {
    label: 'Performance',
    items: [{ key: 'infrastructureOverview' }, { key: 'webOverview' }, { key: 'performance' }],
  },
  /** The drawings, one row per page; see DIAGRAMS above for why they are links. */
  diagrams: {
    label: 'Diagrams',
    items: [],
    links: DIAGRAMS,
  },
  /**
   * How the site looks, and the rules that keep it looking that way, as a
   * section of its own (ADR-016, the addenda on the style section and on its
   * four pages): the Style guide on top, then the colours, the ground and the
   * files the look is built from, each drawn from the design token files
   * when the page is opened. The rules on them are held by the gate, so the
   * pages describe what the tests enforce.
   */
  // #region look-menu
  look: {
    label: 'Style',
    items: [
      { key: 'styleGuide' },
      { key: 'colorStyle', sub: true },
      { key: 'backgroundRibbon', sub: true },
      { key: 'uiArchitecture', sub: true },
    ],
  },
  // #endregion look-menu
  /**
   * Who comes, what is kept about them and how the site is found, as a section of its own
   * on the Style section's pattern (ADR-071 and ADR-053, the addenda of 29 September): the
   * landing page on top, then its pages; the fourth, Search Console step by step, since 1.0.3.50.
   */
  traffic: {
    label: 'Site traffic',
    items: [
      { key: 'siteTraffic' },
      { key: 'trafficWho', sub: true },
      { key: 'trafficKept', sub: true },
      { key: 'trafficFound', sub: true },
      { key: 'trafficSearchConsole', sub: true },
    ],
  },
  hosting: {
    label: 'Hosting',
    items: [{ key: 'hosting' }, { key: 'bicep', sub: true }],
  },
  /**
   * What the AI wrote, what the owner decided and what governed it, beside
   * Hosting rather than under About: a comparison or explanation page is not a
   * decision record, and a subject this size sits at the top level.
   */
  builtWithAi: {
    label: 'Built with AI',
    items: [{ key: 'builtWithAi' }],
  },
  cicd: {
    label: 'CI/CD',
    items: [{ key: 'cicd' }],
    links: [LINKS.ciRuns],
  },
  practices: {
    label: 'Best Practices',
    items: [{ key: 'practices' }, { key: 'sealed' }, { key: 'security' }],
  },
  // #region records-menu
  /**
   * Every decision record, in the order they were decided, numbered to match
   * the file each one serves. They used to hang off the four topic sections as
   * sub-rows, which put eighteen under Best Practices alone and turned the
   * sidebar into a wall. Twenty-seven of anything is an index, not a submenu.
   * Since 1.0.0.135 this index is not special: every section in the sidebar is
   * a closed details, and SideNav is where that happens.
   */
  records: {
    label: 'Decision Records',
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
      { key: 'adrTheName' },
      { key: 'adrSecondManifest' },
      { key: 'adrReset' },
      { key: 'adrRoomAccount' },
      { key: 'adrLockout' },
      { key: 'adrCoverage' },
      { key: 'adrWhereGatesLive' },
      { key: 'adrPublicFace' },
      { key: 'adrOneWrite' },
      { key: 'adrBrokenWindows' },
      { key: 'adrStaleListing' },
      { key: 'adrRecordAddress' },
      { key: 'adrPartitionKey' },
      { key: 'adrSecondStore' },
      { key: 'adrPortsWait' },
      { key: 'adrAccountsDocuments' },
      { key: 'adrStoreVisible' },
      { key: 'adrBackends' },
      { key: 'adrMeasuringStores' },
      { key: 'adrCosmosExplained' },
      { key: 'adrOneContainer' },
      { key: 'adrProof' },
      { key: 'adrFiveMinuteGate' },
      { key: 'adrSecondAddress' },
      { key: 'adrThreeReaders' },
      { key: 'adrActivity' },
      { key: 'adrSecrets' },
      { key: 'adrKeptLogs' },
      { key: 'adrHighlighting' },
      { key: 'adrRules' },
      { key: 'adrOpenApi' },
      { key: 'adrPageStatus' },
      { key: 'adrMachines' },
      { key: 'adrOnePlan' },
      { key: 'adrAdminProduct' },
      { key: 'adrGlassLook' },
      { key: 'adrLandingPage' },
      { key: 'adrTweaks' },
      { key: 'adrComponentFolders' },
      { key: 'adrKeptAwake' },
      { key: 'adrCompositionRoot' },
      { key: 'adrAzureCosts' },
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
