import { describe, expect, it } from 'vitest';
import { inlineScriptHashes, pageNonce, pagePolicy, securityHeaders } from './security';

describe('the headers a drawn page carries', () => {
  it("lets through the page's own inline script by its hash, and no script with a src or a block of data", async () => {
    const page = `<script>console.log('the preload hints');</script>
      <script type="application/ld+json">{"name":"data"}</script>
      <script type="module" src="/assets/index.js"></script>
      <script type="application/json" id="yard-first-load">{}</script>`;
    const digest = await crypto.subtle.digest(
      'SHA-256',
      new TextEncoder().encode("console.log('the preload hints');")
    );
    const expected = `'sha256-${btoa(String.fromCharCode(...new Uint8Array(digest)))}'`;
    expect(await inlineScriptHashes(page)).toEqual([expected]);
  });

  it('allows no inline script but those, files from the site only, and no framing by another site', () => {
    const policy = pagePolicy(["'sha256-abc'"], 'n0nce');
    const scripts = policy
      .split(';')
      .map((part) => part.trim())
      .find((part) => part.startsWith('script-src '))!;
    expect(scripts).toBe("script-src 'self' 'sha256-abc' 'nonce-n0nce'");
    expect(policy).toContain("frame-ancestors 'none'");
    expect(policy).toContain("object-src 'none'");
    expect(policy).not.toContain('unsafe-eval');
  });

  it('carries every header, HSTS with its subdomains and never preloaded, and a new nonce for each page', () => {
    const headers = securityHeaders([], pageNonce());
    expect(Object.keys(headers).sort()).toEqual([
      'Content-Security-Policy',
      'Cross-Origin-Opener-Policy',
      'Permissions-Policy',
      'Referrer-Policy',
      'Strict-Transport-Security',
      'X-Content-Type-Options',
      'X-Frame-Options',
    ]);
    expect(headers['Strict-Transport-Security']).toBe('max-age=31536000; includeSubDomains');
    expect(pageNonce()).not.toBe(pageNonce());
  });
});
