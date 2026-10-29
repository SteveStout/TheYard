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
} as const satisfies Record<string, RecordEntry>;
