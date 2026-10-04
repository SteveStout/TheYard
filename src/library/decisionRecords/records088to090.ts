/**
 * Does:      Lists the decision records numbered 088 to 090, in number order, as data.
 * Does not:  Hold any other record or document, say which sidebar section shows a record, or draw anything.
 * Used by:   records.ts.
 */
// One run of the decision records, kept whole as written: a title, a sidebar label, the
// address the API serves it at, and its number. records.ts joins the runs back into one list
// in number order.
import type { RecordEntry } from '../records';

/** The decision records numbered 088 to 090, keyed by each one's name in code. */
export const RECORDS_088_TO_090 = {
  adrOnionAndSolid: {
    title: 'ADR: Onion and SOLID, how this codebase holds them',
    menuLabel: 'ADR: Onion and SOLID, how this codebase holds them',
    url: '/api/docs/adr-onion-and-solid',
    kind: 'adr',
    number: '088',
  },
  adrTechnologyVersions: {
    title: 'ADR: Technology versions',
    menuLabel: 'ADR: Technology versions',
    url: '/api/docs/adr-technology-versions',
    kind: 'adr',
    number: '089',
  },
  adrDotnet10: {
    title: 'ADR: Staying on .NET 10, and on xUnit v2 for now',
    menuLabel: 'ADR: Staying on .NET 10, and on xUnit v2 for now',
    url: '/api/docs/adr-dotnet-10',
    kind: 'adr',
    number: '090',
  },
} as const satisfies Record<string, RecordEntry>;
