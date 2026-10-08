/**
 * Does:      Lists the decision records numbered 088 to 096, in number order, as data.
 * Does not:  Hold any other record or document, say which sidebar section shows a record, or draw anything.
 * Used by:   records.ts.
 */
// One run of the decision records, kept whole as written: a title, a sidebar label, the
// address the API serves it at, and its number. records.ts joins the runs back into one list
// in number order.
import type { RecordEntry } from '../records';

/** The decision records numbered 088 to 096, keyed by each one's name in code. */
export const RECORDS_088_TO_096 = {
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
  adrDocsFolders: {
    title: 'ADR: One folder per sidebar section',
    menuLabel: 'ADR: One folder per sidebar section',
    url: '/api/docs/adr-docs-folders',
    kind: 'adr',
    number: '091',
  },
  adrRenderAtBuildTime: {
    title: 'ADR: The landing page rendered at build time, server rendering as the goal',
    menuLabel: 'ADR: The landing page rendered at build time, server rendering as the goal',
    url: '/api/docs/adr-render-at-build-time',
    kind: 'adr',
    number: '092',
  },
  adrRequestPipeline: {
    title: 'ADR: The order of the request pipeline',
    menuLabel: 'ADR: The order of the request pipeline',
    url: '/api/docs/adr-request-pipeline',
    kind: 'adr',
    number: '093',
  },
  adrCompiledBeforeItShips: {
    title: 'ADR: Compiled before it ships',
    menuLabel: 'ADR: Compiled before it ships',
    url: '/api/docs/adr-compiled-before-it-ships',
    kind: 'adr',
    number: '094',
  },
  adrBlueGreenMeasuredAndHeld: {
    title: 'ADR: Blue-green on a $13 plan, measured and held',
    menuLabel: 'ADR: Blue-green on a $13 plan, measured and held',
    url: '/api/docs/adr-blue-green-measured-and-held',
    kind: 'adr',
    number: '095',
  },
  adrARenderingServiceBesideTheApi: {
    title: 'ADR: A rendering service beside the API',
    menuLabel: 'ADR: A rendering service beside the API',
    url: '/api/docs/adr-a-rendering-service-beside-the-api',
    kind: 'adr',
    number: '096',
  },
} as const satisfies Record<string, RecordEntry>;
