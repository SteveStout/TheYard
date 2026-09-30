/**
 * Does:      Lists every other document, as data: the overviews, the Bicep file, the changelog, the Author page.
 * Does not:  Hold a decision record (records.ts does), say which sidebar section shows a page, or draw anything.
 * Used by:   documents.ts.
 */
import type { DocEntry } from './documents';

/** Every page here is something other than a record, so none carries a record's number. */
type PageEntry = DocEntry & { kind: 'overview' | 'infra' | 'changelog' };

// #region pages
// Every other document the sidebar can open. The same shape as the records:
// each URL ends in the slug the API serves it under, and DocumentationCatalogTests
// holds this file and records.ts to DocumentationCatalog.cs in both directions.
export const PAGES = {
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
  sqlVsCosmos: {
    title: 'SQL Server and Cosmos DB, side by side',
    menuLabel: 'Side by side, row by row',
    url: '/api/docs/sql-vs-cosmos',
    kind: 'overview',
  },
  hosting: {
    title: 'Hosting',
    menuLabel: 'Hosting overview',
    url: '/api/docs/hosting',
    kind: 'overview',
  },
  bicep: {
    title: 'Infrastructure (Bicep)',
    menuLabel: 'Infrastructure (Bicep)',
    url: '/api/docs/bicep',
    kind: 'infra',
  },
  cicd: { title: 'CI/CD', menuLabel: 'CI/CD overview', url: '/api/docs/cicd', kind: 'overview' },
  practices: {
    title: 'Best Practices',
    menuLabel: 'Best practices overview',
    url: '/api/docs/practices',
    kind: 'overview',
  },
  sealed: {
    title: 'Sealed by default',
    menuLabel: 'Sealed by default',
    url: '/api/docs/sealed',
    kind: 'overview',
  },
  security: {
    title: 'Security',
    menuLabel: 'Security',
    url: '/api/docs/security',
    kind: 'overview',
  },
  changelog: {
    title: 'Changelog',
    menuLabel: 'Version history',
    url: '/api/docs/changelog',
    kind: 'changelog',
  },
  startHere: {
    title: 'Start here',
    menuLabel: 'Start here (new developer)',
    url: '/api/docs/start-here',
    kind: 'overview',
  },
  architecture: {
    title: 'App Architecture',
    menuLabel: 'Architecture overview',
    url: '/api/docs/architecture',
    kind: 'overview',
  },
  styleGuide: {
    title: 'Style guide',
    menuLabel: 'Style guide',
    url: '/api/docs/style-guide',
    kind: 'overview',
  },
  colorStyle: {
    title: 'Colour and style',
    menuLabel: 'Colour and style',
    url: '/api/docs/color-style',
    kind: 'overview',
  },
  backgroundRibbon: {
    title: 'Background and ribbon',
    menuLabel: 'Background and ribbon',
    url: '/api/docs/background-ribbon',
    kind: 'overview',
  },
  uiArchitecture: {
    title: 'UI architecture',
    menuLabel: 'UI architecture',
    url: '/api/docs/ui-architecture',
    kind: 'overview',
  },
  style: {
    title: 'Coding and Commenting Style',
    menuLabel: 'Coding and comments',
    url: '/api/docs/style',
    kind: 'overview',
  },
  author: {
    title: 'About Steven',
    menuLabel: 'About Steven',
    url: '/api/docs/author',
    kind: 'overview',
  },
  aiDevelopment: {
    title: 'How this was built',
    menuLabel: 'How this was built',
    url: '/api/docs/ai-development',
    kind: 'overview',
  },
  builtWithAi: {
    title: 'Built with AI',
    menuLabel: 'What the AI wrote, what I decided',
    url: '/api/docs/built-with-ai',
    kind: 'overview',
  },
  infrastructureOverview: {
    title: 'Infrastructure overview',
    menuLabel: 'Infrastructure overview',
    url: '/api/docs/infrastructure-overview',
    kind: 'overview',
  },
  webOverview: {
    title: 'Web overview',
    menuLabel: 'Web overview',
    url: '/api/docs/web-overview',
    kind: 'overview',
  },
  performance: {
    title: 'Performance',
    menuLabel: 'Performance overview',
    url: '/api/docs/performance',
    kind: 'overview',
  },
  siteTraffic: {
    title: 'Site traffic',
    menuLabel: 'Site traffic',
    url: '/api/docs/site-traffic',
    kind: 'overview',
  },
  trafficWho: {
    title: 'Who comes',
    menuLabel: 'Who comes',
    url: '/api/docs/traffic-who',
    kind: 'overview',
  },
  trafficKept: {
    title: 'What is kept',
    menuLabel: 'What is kept',
    url: '/api/docs/traffic-kept',
    kind: 'overview',
  },
  trafficFound: {
    title: 'Being found',
    menuLabel: 'Being found',
    url: '/api/docs/traffic-found',
    kind: 'overview',
  },
  trafficSearchConsole: {
    title: 'Search Console, step by step',
    menuLabel: 'Search Console',
    url: '/api/docs/traffic-search-console',
    kind: 'overview',
  },
} as const satisfies Record<string, PageEntry>;
// #endregion pages
