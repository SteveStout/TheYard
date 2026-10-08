/**
 * Does:      Is the one module the rendering service's build starts from: the renderer, and the page reader the readiness
 *            probe uses.
 * Does not:  Listen for requests (server.mjs does) or hold any rule of its own.
 * Used by:   the build in render/vite.config.ts, whose output server.mjs imports.
 */
export { createRenderer, RENDERED_HEADER, type OwnBuild, type Render } from './render';
export { servedPages } from './page';
