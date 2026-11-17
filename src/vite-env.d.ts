/// <reference types="vite/client" />

/** The release facts the build reads from the changelog and the findings file (vite.config.ts, src/lib/releaseFacts.ts). */
declare module 'virtual:release-facts' {
  const facts: import('./lib/releaseFacts').ReleaseFacts;
  export default facts;
}
