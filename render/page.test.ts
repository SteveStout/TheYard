import { describe, expect, it } from 'vitest';
import { KEPT_ORIGINS, keptPosts } from './page';

/** A stand-in for fetch that counts its reads per origin and answers with one post. */
function counting() {
  const reads = new Map<string, number>();
  const read = (async (url: string | URL | Request) => {
    const origin = new URL(String(url)).origin;
    reads.set(origin, (reads.get(origin) ?? 0) + 1);
    return new Response(JSON.stringify({ posts: [{ slug: 's', title: 't', summary: 'u' }] }));
  }) as typeof fetch;
  return { read, reads };
}

// CodeRabbit on #56: the kept posts were a Map with no bound, one entry for every API origin ever asked.
describe('the posts the renderer keeps (ADR-132)', () => {
  it('reads an origin once while its posts are fresh', async () => {
    const { read, reads } = counting();
    const postsAt = keptPosts(read);
    await postsAt('https://a.example');
    await postsAt('https://a.example');
    expect(reads.get('https://a.example')).toBe(1);
  });

  it('keeps at most KEPT_ORIGINS origins, dropping the one asked longest ago', async () => {
    const { read, reads } = counting();
    const postsAt = keptPosts(read);
    const origins = Array.from({ length: KEPT_ORIGINS + 1 }, (_, n) => `https://o${n}.example`);
    for (const origin of origins) await postsAt(origin);
    await postsAt(origins[KEPT_ORIGINS]);
    await postsAt(origins[1]);
    expect(reads.get(origins[1])).toBe(1);
    await postsAt(origins[0]);
    expect(reads.get(origins[0])).toBe(2);
  });
});
