/**
 * Does:      Says what each sidebar section holds: its documents in order (MENUS), and the rows that open in a new tab (LINKS, API_REFERENCE, DIAGRAMS).
 * Does not:  Order the sections or pick their icons (src/lib/siteMap.ts does), or hold a document itself (records.ts and pages.ts do).
 * Used by:   SideNav.tsx, Landing.tsx, Header.tsx, sections.test.ts.
 */
import type { MenuVariant } from '../lib/siteMap';
import type { DocKey } from './documents';
import { RECORDS } from './records';

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
 * Every drawing in the catalogue, on its own page (ADR: Every diagram opens on its own page, the
 * addendum on the section), in the order a reader meets the system: the whole, the data, the schema,
 * the two sites, the two stores, the look, then the rings, a bid's walk through them and the port.
 * DocumentationCatalog.Diagrams on the server is the authority for which drawings exist, and a test
 * holds this list to it, so a drawing cannot have a page without a row or a row without a page.
 */
export const DIAGRAMS: readonly MenuLink[] = [
  { label: 'Infrastructure', href: '/api/docs/diagrams/infrastructure' },
  { label: 'Data flow', href: '/api/docs/diagrams/dataflow' },
  { label: 'The database', href: '/api/docs/diagrams/erd' },
  { label: 'The two sites', href: '/api/docs/diagrams/two-sites' },
  { label: 'SQL Server vs Cosmos DB', href: '/api/docs/diagrams/sql-vs-cosmos' },
  { label: 'The UI, layer by layer', href: '/api/docs/diagrams/ui-architecture' },
  { label: 'The rings', href: '/api/docs/diagrams/rings' },
  { label: 'One bid through the rings', href: '/api/docs/diagrams/bid-walk' },
  { label: 'The port and its adapters', href: '/api/docs/diagrams/port-adapter' },
];
// #endregion diagrams

export const MENUS: Record<
  MenuVariant,
  { label: string; lead?: readonly MenuLink[]; items: MenuEntry[]; links?: readonly MenuLink[] }
> = {
  /**
   * About is the project: its README and how it was built. The page about him is About
   * Steven, in the Author section below, one page for a person to read; /about stays a
   * served page for search engines and tools that do not run the app, and is not a row
   * here (ADR: The sidebar, the addendum on one page about him).
   */
  about: {
    label: 'About',
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
   * overviews open it, the machines and then the page they
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
   * four pages): the Style guide on top, then the colours, the ground, the
   * files the look is built from, and how the documents themselves are drawn,
   * each read from the build when the page is opened. The rules on them are held by the gate, so the
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
      { key: 'documentStyle', sub: true },
    ],
  },
  // #endregion look-menu
  /**
   * Who comes, what is kept about them and how the site is found, as a section of its own
   * on the Style section's pattern (ADR-071 and ADR-053, the addenda): the
   * landing page on top, then its pages; the fourth, Search Console step by step.
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
    items: [
      { key: 'practices' },
      { key: 'sealed' },
      { key: 'onion' },
      { key: 'security' },
      { key: 'reactServerRendering' },
    ],
  },
  // #region records-menu
  /**
   * Every decision record, in the order they were decided, numbered to match the file
   * each one serves: a section of its own, because ninety rows under the topic sections
   * would be a wall. Every section in the sidebar is a closed details (SideNav.tsx).
   * Read from the records themselves in number order, so a new record is in the menu,
   * and in the landing page's count, the moment it is in records.ts.
   */
  records: {
    label: 'Decision Records',
    items: (Object.entries(RECORDS) as [DocKey, { number: string }][])
      .sort(([, a], [, b]) => a.number.localeCompare(b.number))
      .map(([key]) => ({ key })),
  },
  // #endregion records-menu
  // #region menu-changelog
  /** One item on purpose: one file, one sentence per version (ADR-012). */
  changelog: { label: 'Changelog', items: [{ key: 'changelog' }] },
  // #endregion menu-changelog
};
