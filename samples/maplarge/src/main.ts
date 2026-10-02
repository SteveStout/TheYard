/**
 * The entry point for the page. It reads like a table of contents: each line starts one part, in
 * order, and the comment beside it names the file that holds the details. Program.cs does the same
 * for the server.
 */

import { currentState, navigate, render, startNavigation } from './navigation.js';
import { connectControls } from './ui/controls.js';
import { findPageElements } from './ui/pageElements.js';
import { showVersion } from './ui/versionFooter.js';

// #region composition
const page = findPageElements();                 // ui/pageElements.ts: the elements index.html must have
startNavigation(page);                           // navigation.ts: the address bar, the two views, Back and Forward
connectControls(page, navigate, currentState);   // ui/controls.ts: open, close, the tabs, view links, Escape
showVersion(page.footer, navigate);              // ui/versionFooter.ts: the build version in the footer
render();                                        // navigation.ts: draw what the address asks for
// #endregion composition
