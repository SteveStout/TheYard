import { describe, expect, it } from 'vitest';
import { KEPT_POSTS, keptPost } from './page';

/** A stand-in for fetch that counts its reads per address and answers each post's route with that post, or a 404. */
function counting() {
  const reads = new Map<string, number>();
  const read = (async (url: string | URL | Request) => {
    const address = String(url);
    reads.set(address, (reads.get(address) ?? 0) + 1);
    const slug = address.split('/').pop()!;
    if (slug === 'no-such-post') return new Response('{}', { status: 404 });
    return new Response(
      JSON.stringify({
        slug,
        title: 't',
        summary: 'u',
        images: [{ position: 1, url: '/blog/images/a.webp' }],
      })
    );
  }) as typeof fetch;
  return { read, reads };
}

// The kept heads are a Map with a bound, one entry for each post a shared link names, never one for every address ever asked.
describe('the post heads the renderer keeps (ADR-132)', () => {
  it('reads a post once while its head is fresh, and keeps its first image', async () => {
    const { read, reads } = counting();
    const postAt = keptPost(read);
    expect((await postAt('https://a.example', 'a-post'))?.image?.url).toBe('/blog/images/a.webp');
    await postAt('https://a.example', 'a-post');
    expect(reads.get('https://a.example/api/blog/posts/a-post')).toBe(1);
    expect(await postAt('https://a.example', 'no-such-post')).toBeNull();
  });

  it('keeps at most KEPT_POSTS heads, dropping the one asked longest ago', async () => {
    const { read, reads } = counting();
    const postAt = keptPost(read);
    const slugs = Array.from({ length: KEPT_POSTS + 1 }, (_, n) => `post-${n}`);
    for (const slug of slugs) await postAt('https://a.example', slug);
    await postAt('https://a.example', slugs[1]);
    expect(reads.get('https://a.example/api/blog/posts/post-1')).toBe(1);
    await postAt('https://a.example', slugs[0]);
    expect(reads.get('https://a.example/api/blog/posts/post-0')).toBe(2);
  });
});
