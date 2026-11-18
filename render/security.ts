/**
 * Does:      The security headers for a page the rendering service draws (ADR: Every LinkedIn post is also a blog post, the
 *            addendum on the headers), the same policy the API sends (api/TheYard.Api/Shared/Wiring/SecurityHeaders.cs,
 *            held to this file by SecurityHeadersTests): the page's own inline scripts let through by their hashes, any
 *            script React streams by the page's nonce, and nothing else inline.
 * Does not:  Decide what the page is or how long the edge keeps it (render.ts does).
 * Used by:   render.ts, security.test.ts.
 */

// #region the-headers
export const HSTS = 'max-age=31536000; includeSubDomains';
export const REFERRER_POLICY = 'strict-origin-when-cross-origin';
export const PERMISSIONS_POLICY =
  'accelerometer=(), autoplay=(), camera=(), display-capture=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), midi=(), payment=(), usb=(), browsing-topics=()';
export const OPENER_POLICY = 'same-origin';
export const EVERY_OTHER_SOURCE =
  "style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https://github.com https://raw.githubusercontent.com; font-src 'self' data:; connect-src 'self'; frame-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; manifest-src 'self'; worker-src 'self' blob:";
// #endregion the-headers

const INLINE_SCRIPT = /<script([^>]*)>([\s\S]*?)<\/script>/gi;

/** Bytes as base64, with the web platform's own btoa: the renderer uses no Node API, so it can run on a worker too. */
function base64(bytes: Uint8Array): string {
  return btoa(Array.from(bytes, (byte) => String.fromCharCode(byte)).join(''));
}

/** The 'sha256-...' source of every script in the page that runs from its own text: not one with a src, not a block of data. */
export async function inlineScriptHashes(html: string): Promise<string[]> {
  const hashes: string[] = [];
  for (const [, attributes, body] of html.matchAll(INLINE_SCRIPT)) {
    if (/src=/i.test(attributes) || /json/i.test(attributes)) continue;
    const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(body));
    const source = `'sha256-${base64(new Uint8Array(digest))}'`;
    if (!hashes.includes(source)) hashes.push(source);
  }
  return hashes;
}

/** A nonce for one page: sixteen random bytes, the policy's and React's for whatever it streams. */
export function pageNonce(): string {
  return base64(crypto.getRandomValues(new Uint8Array(16)));
}

/** The page's policy: files from this site, the page's own inline scripts by hash, React's by the nonce, nothing else. */
export function pagePolicy(hashes: readonly string[], nonce: string | null): string {
  const scripts = ["'self'", ...hashes, ...(nonce ? [`'nonce-${nonce}'`] : [])].join(' ');
  return `default-src 'self'; script-src ${scripts}; ${EVERY_OTHER_SOURCE}; frame-ancestors 'none'`;
}

/** Every security header a drawn page carries. */
export function securityHeaders(
  hashes: readonly string[],
  nonce: string | null
): Record<string, string> {
  return {
    'Content-Security-Policy': pagePolicy(hashes, nonce),
    'X-Content-Type-Options': 'nosniff',
    'X-Frame-Options': 'DENY',
    'Referrer-Policy': REFERRER_POLICY,
    'Permissions-Policy': PERMISSIONS_POLICY,
    'Cross-Origin-Opener-Policy': OPENER_POLICY,
    'Strict-Transport-Security': HSTS,
  };
}
