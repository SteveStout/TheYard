/**
 * Does:      Lists the decision records numbered 088 to 120, in number order, as data.
 * Does not:  Hold any other record or document, say which sidebar section shows a record, or draw anything.
 * Used by:   records.ts.
 */
// One run of the decision records, kept whole as written: a title, a sidebar label, the
// address the API serves it at, and its number. records.ts joins the runs back into one list
// in number order.
import type { RecordEntry } from '../records';

/** The decision records numbered 088 to 120, keyed by each one's name in code. */
export const RECORDS_088_TO_120 = {
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
  adrTheEditorIsSetUpByTheRepository: {
    title: 'ADR: The editor is set up by the repository',
    menuLabel: 'ADR: The editor is set up by the repository',
    url: '/api/docs/adr-the-editor-is-set-up-by-the-repository',
    kind: 'adr',
    number: '097',
  },
  adrTheGateRunsWhereTheMemoryIs: {
    title: 'ADR: The gate runs where the memory is',
    menuLabel: 'ADR: The gate runs where the memory is',
    url: '/api/docs/adr-the-gate-runs-where-the-memory-is',
    kind: 'adr',
    number: '098',
  },
  adrTheResumeHasAnAddress: {
    title: 'ADR: The resume has an address',
    menuLabel: 'ADR: The resume has an address',
    url: '/api/docs/adr-the-resume-has-an-address',
    kind: 'adr',
    number: '099',
  },
  adrOneReadingForTheFirstScreen: {
    title: 'ADR: One reading for the first screen',
    menuLabel: 'ADR: One reading for the first screen',
    url: '/api/docs/adr-one-reading-for-the-first-screen',
    kind: 'adr',
    number: '100',
  },
  adrTheAppServicePlanReadEveryMinute: {
    title: 'ADR: The App Service plan, read every minute',
    menuLabel: 'ADR: The App Service plan, read every minute',
    url: '/api/docs/adr-the-app-service-plan-read-every-minute',
    kind: 'adr',
    number: '101',
  },
  adrOneFrameForEveryResource: {
    title: 'ADR: One frame for every resource',
    menuLabel: 'ADR: One frame for every resource',
    url: '/api/docs/adr-one-frame-for-every-resource',
    kind: 'adr',
    number: '102',
  },
  adrTheStoreDoesTheWork: {
    title: 'ADR: The store does the work',
    menuLabel: 'ADR: The store does the work',
    url: '/api/docs/adr-the-store-does-the-work',
    kind: 'adr',
    number: '103',
  },
  adrMemoryFollowsTraffic: {
    title: 'ADR: Memory follows traffic',
    menuLabel: 'ADR: Memory follows traffic',
    url: '/api/docs/adr-memory-follows-traffic',
    kind: 'adr',
    number: '104',
  },
  adrOneWebAppForEveryNetProcess: {
    title: 'ADR: One web app for every .NET process',
    menuLabel: 'ADR: One web app for every .NET process',
    url: '/api/docs/adr-one-web-app-for-every-net-process',
    kind: 'adr',
    number: '105',
  },
  adrUnusedResourcesAreDeleted: {
    title: 'ADR: Unused resources are deleted',
    menuLabel: 'ADR: Unused resources are deleted',
    url: '/api/docs/adr-unused-resources-are-deleted',
    kind: 'adr',
    number: '106',
  },
  adrTheOnionCutIntoVerticalSlices: {
    title: 'ADR: The onion cut into vertical slices',
    menuLabel: 'ADR: The onion cut into vertical slices',
    url: '/api/docs/adr-the-onion-cut-into-vertical-slices',
    kind: 'adr',
    number: '107',
  },
  adrTaggedLinksWhoOpenedWhat: {
    title: 'ADR: Tagged links, who opened what',
    menuLabel: 'ADR: Tagged links, who opened what',
    url: '/api/docs/adr-tagged-links-who-opened-what',
    kind: 'adr',
    number: '108',
  },
  adrTheNightlyFunctionOnTheAdminTab: {
    title: 'ADR: The Nightly Function on the Admin tab',
    menuLabel: 'ADR: The Nightly Function on the Admin tab',
    url: '/api/docs/adr-the-nightly-function-on-the-admin-tab',
    kind: 'adr',
    number: '109',
  },
} as const satisfies Record<string, RecordEntry>;
