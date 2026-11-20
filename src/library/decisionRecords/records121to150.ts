/**
 * Does:      Lists the decision records numbered 121 to 150, in number order, as data.
 * Does not:  Hold any other record or document, say which sidebar section shows a record, or draw anything.
 * Used by:   records.ts.
 */
// One run of the decision records, kept whole as written: a title, a sidebar label, the
// address the API serves it at, and its number. records.ts joins the runs back into one list
// in number order.
import type { RecordEntry } from '../records';

/** The decision records numbered 121 to 150, keyed by each one's name in code. */
export const RECORDS_121_TO_150 = {
  adrForeman: {
    title: 'ADR: The Foreman, a place to start',
    menuLabel: 'ADR: The Foreman, a place to start',
    url: '/api/docs/adr-the-foreman-a-place-to-start',
    kind: 'adr',
    number: '121',
  },
  adrQualityRatchet: {
    title: 'ADR: The numbers that may only fall',
    menuLabel: 'ADR: The numbers that may only fall',
    url: '/api/docs/adr-the-numbers-that-may-only-fall',
    kind: 'adr',
    number: '122',
  },
  adrReviewRoute: {
    title: 'ADR: Ten minutes, seven stops',
    menuLabel: 'ADR: Ten minutes, seven stops',
    url: '/api/docs/adr-ten-minutes-seven-stops',
    kind: 'adr',
    number: '123',
  },
  adrNetworkLog: {
    title: 'ADR: The network log drafts, Steve sends',
    menuLabel: 'ADR: The network log drafts, Steve sends',
    url: '/api/docs/adr-the-network-log-drafts-steve-sends',
    kind: 'adr',
    number: '124',
  },
  adrNightlyPicker: {
    title: 'ADR: The nightly picker, code gates and the model ranks',
    menuLabel: 'ADR: The nightly picker, code gates and the model ranks',
    url: '/api/docs/adr-the-nightly-picker-code-gates-and-the-model-ranks',
    kind: 'adr',
    number: '125',
  },
  adrRecommendationsPage: {
    title: 'ADR: The recommendations have a page',
    menuLabel: 'ADR: The recommendations have a page',
    url: '/api/docs/adr-the-recommendations-have-a-page',
    kind: 'adr',
    number: '126',
  },
  adrBoxReportsItself: {
    title: 'ADR: The box reports itself',
    menuLabel: 'ADR: The box reports itself',
    url: '/api/docs/adr-the-box-reports-itself',
    kind: 'adr',
    number: '127',
  },
  adrLiveDials: {
    title: 'ADR: Live dials where the source allows',
    menuLabel: 'ADR: Live dials where the source allows',
    url: '/api/docs/adr-live-dials-where-the-source-allows',
    kind: 'adr',
    number: '128',
  },
  adrCodeRabbit: {
    title: 'ADR: CodeRabbit, one rung at a time',
    menuLabel: 'ADR: CodeRabbit, one rung at a time',
    url: '/api/docs/adr-coderabbit-one-rung-at-a-time',
    kind: 'adr',
    number: '129',
  },
  adrQualityYouCanWatch: {
    title: 'ADR: Quality you can watch',
    menuLabel: 'ADR: Quality you can watch',
    url: '/api/docs/adr-quality-you-can-watch',
    kind: 'adr',
    number: '130',
  },
  adrLibraryDocsAtThePinnedVersion: {
    title: 'ADR: Library docs at the pinned version',
    menuLabel: 'ADR: Library docs at the pinned version',
    url: '/api/docs/adr-library-docs-at-the-pinned-version',
    kind: 'adr',
    number: '131',
  },
  adrAHookOnEveryPage: {
    title: 'ADR: A hook on every page',
    menuLabel: 'ADR: A hook on every page',
    url: '/api/docs/adr-a-hook-on-every-page',
    kind: 'adr',
    number: '132',
  },
  adrOneSqlDatabase: {
    title: 'ADR: One SQL database, one schema per area',
    menuLabel: 'ADR: One SQL database, one schema per area',
    url: '/api/docs/adr-one-sql-database-one-schema-per-area',
    kind: 'adr',
    number: '133',
  },
  adrAByteBudgetForTheFirstLoad: {
    title: 'ADR: A byte budget for the first load',
    menuLabel: 'ADR: A byte budget for the first load',
    url: '/api/docs/adr-a-byte-budget-for-the-first-load',
    kind: 'adr',
    number: '134',
  },
  adrAnEmptyRunIsAFailedRun: {
    title: 'ADR: An empty run is a failed run',
    menuLabel: 'ADR: An empty run is a failed run',
    url: '/api/docs/adr-an-empty-run-is-a-failed-run',
    kind: 'adr',
    number: '135',
  },
  adrAPullRequestOfAiWrittenCodeIsSizedToTheGate: {
    title: 'ADR: A pull request of AI-written code is sized to the gate',
    menuLabel: 'ADR: A pull request of AI-written code is sized to the gate',
    url: '/api/docs/adr-a-pull-request-of-ai-written-code-is-sized-to-the-gate',
    kind: 'adr',
    number: '136',
  },
  adrOneTrafficCopForMain: {
    title: 'ADR: One traffic cop for main',
    menuLabel: 'ADR: One traffic cop for main',
    url: '/api/docs/adr-one-traffic-cop-for-main',
    kind: 'adr',
    number: '137',
  },
  adrTheReviewRouteGivesAReasonBeforeItAsks: {
    title: 'ADR: The review route gives a reason before it asks',
    menuLabel: 'ADR: The review route gives a reason before it asks',
    url: '/api/docs/adr-the-review-route-gives-a-reason-before-it-asks',
    kind: 'adr',
    number: '138',
  },
  adrTheSystemWatchesItsScheduledTasksAndHealsWhatItMay: {
    title: 'ADR: The system watches its scheduled tasks and heals what it may',
    menuLabel: 'ADR: The system watches its scheduled tasks and heals what it may',
    url: '/api/docs/adr-the-system-watches-its-scheduled-tasks-and-heals-what-it-may',
    kind: 'adr',
    number: '139',
  },
  adrTheStackHasAPageOfItsOwn: {
    title: 'ADR: The stack has a page of its own',
    menuLabel: 'ADR: The stack has a page of its own',
    url: '/api/docs/adr-the-stack-has-a-page-of-its-own',
    kind: 'adr',
    number: '140',
  },
  adrTheSalesPagesSetTheSitesLook: {
    title: "ADR: The sales pages set the site's look",
    menuLabel: "ADR: The sales pages set the site's look",
    url: '/api/docs/adr-the-sales-pages-set-the-sites-look',
    kind: 'adr',
    number: '@ADR@',
  },
} as const satisfies Record<string, RecordEntry>;
