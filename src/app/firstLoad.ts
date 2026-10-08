/**
 * Does:      Writes the rendering service's first load into the page as JSON, and reads it back in the browser.
 * Does not:  Decide what goes in it (render/loaders.ts does) or hand it to the hooks (TheYard.tsx does).
 * Used by:   mount.tsx, entry-server.tsx.
 */
import type { FirstLoad } from '../hooks/useFirstLoad';

// #region the-script
/** The id of the script element the first load travels in, beside #root. */
export const FIRST_LOAD_ID = 'yard-first-load';

/**
 * The first load as a script element the browser does not run: type
 * application/json is data. Every "<" is written as its escape, so nothing in a
 * vehicle's description or a document can close the element early and start
 * markup of its own; JSON.parse reads the escape back as the same character.
 */
export function firstLoadScript(load: FirstLoad): string {
  const json = JSON.stringify(load).replace(/</g, '\\u003c');
  return `<script type="application/json" id="${FIRST_LOAD_ID}">${json}</script>`;
}

/**
 * The first load the page carries, or null when it carries none (the build's
 * landing page, the development server, or a page the service gave up on). A
 * script that does not parse is treated as none: the browser draws as it always did.
 */
export function readFirstLoad(): FirstLoad | null {
  const element = document.getElementById(FIRST_LOAD_ID);
  if (!element?.textContent) return null;
  try {
    return JSON.parse(element.textContent) as FirstLoad;
  } catch {
    return null;
  }
}
// #endregion the-script
