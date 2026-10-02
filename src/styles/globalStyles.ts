/**
 * Does:      Loads the site-wide stylesheets, once, in the order they must load.
 * Does not:  Hold a style of its own; each sheet below is named for what it controls.
 * Used by:   main.tsx.
 */

// #region load-order
// The order matters. fonts.css declares the face before anything asks for it. colors, sizes,
// typography and effects define every design value as a variable, each file named for what it
// controls. base.css sets the page's defaults from those variables. panels.css and
// code-highlight.css draw shared looks in the same variables, so they come after them.
import './fonts.css';
import './colors.css';
import './sizes.css';
import './typography.css';
import './effects.css';
import './base.css';
import './panels.css';
import './code-highlight.css';
// #endregion load-order
