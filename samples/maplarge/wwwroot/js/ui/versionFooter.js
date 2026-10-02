/**
 * Shows the build version in the page footer, with a link to the readme, so anyone looking at the
 * page can tell exactly which build they are on.
 */
import * as api from '../lib/api.js';
import { buildElement, replaceContents } from './elements.js';
/**
 * Asks the API for the version and commit, and writes them into the footer. If the API cannot
 * answer, the footer still says The Shed.
 * @param footer the footer element from index.html
 * @param navigate the one way the state changes, used by the "about this build" link
 */
export function showVersion(footer, navigate) {
    api.version().then((version) => {
        // A real link, so it works from the keyboard and in a new tab; a normal click opens the readme
        // in the dialog without a reload.
        const about = buildElement('a', { href: '?view=docs&doc=readme', onclick: (event) => { event.preventDefault(); navigate({ view: 'docs', doc: 'readme' }); } }, 'about this build');
        replaceContents(footer, `The Shed ${version.version} @ ${version.commit}`, ' · ', about);
    }).catch(() => replaceContents(footer, 'The Shed'));
}
