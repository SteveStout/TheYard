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
} as const satisfies Record<string, RecordEntry>;
