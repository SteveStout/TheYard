/**
 * Does:      Draws the site for one address on the rendering service, as a stream of HTML, from the answers the service
 *            read first.
 * Does not:  Read anything itself, know the page around #root, or run in a browser: render/render.ts reads the answers and
 *            writes the page, and mount.tsx takes the markup over in the browser.
 * Used by:   render/render.ts, drawServer.test.ts.
 */
// The edge build of React's server renderer: web streams and timers only, no Node API, so the same draw
// runs on App Service under Node today and in a Cloudflare Worker later (ADR: A rendering service beside the API).
import { renderToReadableStream } from 'react-dom/server.edge';
import type { FirstLoad } from '../hooks/useFirstLoad';
import { firstLoadScript } from './firstLoad';
import { TheYard } from './TheYard';

// #region draw-for-the-service
/**
 * The site at one address as HTML, streamed: the frame first, then whatever a
 * Suspense boundary was waiting on (the Admin tab's code, the document renderer)
 * as it arrives. Effects never run here, so nothing is asked of the API while it
 * draws; everything the page needs was read before, into the first load.
 * @param load what the service read for this address
 * @param onError told of a component that threw while drawing; the stream carries on and the browser draws that part
 */
export function drawForTheService(
  load: FirstLoad,
  onError: (error: unknown) => void
): Promise<ReadableStream<Uint8Array>> {
  return renderToReadableStream(<TheYard firstLoad={load} />, { onError });
}

/** The first load as the script element the browser reads it back from (firstLoad.ts). */
export const firstLoadElement = firstLoadScript;
// #endregion draw-for-the-service
